using System.Security.Cryptography;
using TodayChecklist.Models;

namespace TodayChecklist.Services;

public sealed record DataMigrationResult(AppPaths NewPaths, string? ResidualOldPath);

public sealed class DataMigrationService
{
    private const long MinimumFreeSpace = 10 * 1024 * 1024;
    private static readonly string[] OwnedDirectories = ["data", "backups", "logs"];
    private static readonly string[] OwnedFiles = ["settings.json", "settings.json.bak", ".todaychecklist-store.json"];
    private readonly IDataLocationService _locationService;
    private readonly IClock _clock;
    private readonly AtomicJsonFile<StorageMarker> _markerJson = new(StorageValidation.Validate);
    private readonly AtomicJsonFile<AppSettings> _settingsJson = new(DocumentValidator.Validate);

    public DataMigrationService(IDataLocationService locationService, IClock clock)
    {
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DataMigrationResult> MigrateAsync(
        AppPaths source,
        string selectedParent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parent = ValidateSelectedParent(selectedParent);
        var targetRoot = Path.GetFullPath(Path.Combine(parent, "TodayChecklist"));
        EnsureDifferentStore(source.RootDirectory, targetRoot);
        await ValidateSourceOwnershipAsync(source, cancellationToken).ConfigureAwait(false);
        ValidateTargetState(targetRoot);
        EnsureFreeSpace(source.RootDirectory, targetRoot);

        var stageRoot = Path.Combine(parent, $".TodayChecklist.migrating-{Guid.NewGuid():N}");
        var stagePaths = new AppPaths(stageRoot, source.StoreId);
        var committed = false;
        var locatorActivated = false;
        try
        {
            Directory.CreateDirectory(stageRoot);
            CopyOwnedContent(source.RootDirectory, stageRoot);
            await CreateMigrationBackupAsync(source, stagePaths, cancellationToken).ConfigureAwait(false);
            await ValidateCopiedFilesAsync(source.RootDirectory, stageRoot, cancellationToken).ConfigureAwait(false);
            await ValidateStoreAsync(stagePaths, cancellationToken).ConfigureAwait(false);

            CommitStage(stageRoot, targetRoot);
            committed = true;
            var targetPaths = new AppPaths(targetRoot, source.StoreId);
            await ValidateStoreAsync(targetPaths, cancellationToken).ConfigureAwait(false);
            await _locationService.ActivateAsync(targetRoot, source.StoreId, cancellationToken).ConfigureAwait(false);
            locatorActivated = true;

            string? residual = null;
            try
            {
                await ValidateSourceOwnershipAsync(source, cancellationToken).ConfigureAwait(false);
                DeleteOwnedContent(source.RootDirectory, preserveLocator: PathsEqual(source.RootDirectory, _locationService.BootstrapDirectory));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DocumentValidationException)
            {
                residual = source.RootDirectory;
            }

            return new DataMigrationResult(targetPaths, residual);
        }
        catch
        {
            if (!locatorActivated)
            {
                if (committed && PathsEqual(targetRoot, _locationService.BootstrapDirectory))
                {
                    DeleteOwnedContent(targetRoot, preserveLocator: true);
                }
                else
                {
                    TryDeleteCreatedStore(committed ? targetRoot : stageRoot);
                }
            }

            throw;
        }
    }

    public static string GetTargetRoot(string selectedParent)
    {
        return Path.GetFullPath(Path.Combine(ValidateSelectedParent(selectedParent), "TodayChecklist"));
    }

    private static string ValidateSelectedParent(string selectedParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedParent);
        var parent = Path.GetFullPath(selectedParent);
        if (!Path.IsPathFullyQualified(parent) || new Uri(parent).IsUnc || !Directory.Exists(parent))
        {
            throw new DocumentValidationException("请选择可用的本地文件夹；首版不支持网络路径。");
        }

        var attributes = File.GetAttributes(parent);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DocumentValidationException("为了避免误清理，数据位置不能是符号链接或目录联接。");
        }

        var root = Path.GetPathRoot(parent)
            ?? throw new DocumentValidationException("无法识别目标磁盘。");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
        {
            throw new DocumentValidationException("数据位置必须位于可用的本地磁盘或移动磁盘。");
        }

        var target = Path.Combine(parent, "TodayChecklist");
        foreach (var protectedRoot in GetProtectedRoots())
        {
            if (IsSameOrChild(target, protectedRoot))
            {
                throw new DocumentValidationException("不能把数据目录放在 Windows 或 Program Files 等系统目录中。");
            }
        }

        var probe = Path.Combine(parent, $".todaychecklist-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("所选文件夹不可写，请选择其他位置。", exception);
        }
        finally
        {
            if (File.Exists(probe))
            {
                File.Delete(probe);
            }
        }

        return parent;
    }

    private void ValidateTargetState(string targetRoot)
    {
        if (!Directory.Exists(targetRoot))
        {
            return;
        }

        var allowedBootstrapFiles = PathsEqual(targetRoot, _locationService.BootstrapDirectory)
            ? new HashSet<string>(["location.json", "location.last-good.json"], StringComparer.OrdinalIgnoreCase)
            : [];
        var unexpected = new DirectoryInfo(targetRoot)
            .EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
            .Any(item => !allowedBootstrapFiles.Contains(item.Name));
        if (unexpected)
        {
            throw new DocumentValidationException("目标 TodayChecklist 文件夹不是空目录，请选择其他父文件夹。");
        }
    }

    private static void EnsureDifferentStore(string sourceRoot, string targetRoot)
    {
        if (PathsEqual(sourceRoot, targetRoot)
            || IsSameOrChild(targetRoot, sourceRoot)
            || IsSameOrChild(sourceRoot, targetRoot))
        {
            throw new DocumentValidationException("新数据位置不能与当前目录相同，也不能互为父子目录。");
        }
    }

    private static void EnsureFreeSpace(string sourceRoot, string targetRoot)
    {
        var sourceSize = EnumerateOwnedFiles(sourceRoot).Sum(static file => file.Length);
        var driveRoot = Path.GetPathRoot(targetRoot)
            ?? throw new DocumentValidationException("无法识别目标磁盘。");
        var available = new DriveInfo(driveRoot).AvailableFreeSpace;
        var required = checked((sourceSize * 2) + MinimumFreeSpace);
        if (available < required)
        {
            throw new IOException($"目标磁盘空间不足；至少需要 {required:N0} 字节可用空间。");
        }
    }

    private async Task ValidateSourceOwnershipAsync(AppPaths source, CancellationToken cancellationToken)
    {
        if (!File.Exists(source.MarkerFile))
        {
            throw new DocumentValidationException("当前数据目录缺少所有权标记，已拒绝迁移或清理。");
        }

        var marker = await _markerJson.ReadAsync(source.MarkerFile, cancellationToken).ConfigureAwait(false);
        if (marker.StoreId != source.StoreId)
        {
            throw new DocumentValidationException("当前数据目录的所有权标记不匹配。");
        }
    }

    private static void CopyOwnedContent(string sourceRoot, string stageRoot)
    {
        foreach (var directoryName in OwnedDirectories)
        {
            var sourceDirectory = Path.Combine(sourceRoot, directoryName);
            if (Directory.Exists(sourceDirectory))
            {
                CopyDirectory(sourceDirectory, Path.Combine(stageRoot, directoryName));
            }
        }

        foreach (var fileName in OwnedFiles)
        {
            var sourceFile = Path.Combine(sourceRoot, fileName);
            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, Path.Combine(stageRoot, fileName), overwrite: false);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        var sourceInfo = new DirectoryInfo(source);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DocumentValidationException("数据目录包含符号链接或目录联接，已拒绝迁移。");
        }

        Directory.CreateDirectory(destination);
        foreach (var file in sourceInfo.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DocumentValidationException("数据目录包含链接文件，已拒绝迁移。");
            }

            file.CopyTo(Path.Combine(destination, file.Name), overwrite: false);
        }

        foreach (var directory in sourceInfo.EnumerateDirectories())
        {
            CopyDirectory(directory.FullName, Path.Combine(destination, directory.Name));
        }
    }

    private async Task CreateMigrationBackupAsync(
        AppPaths source,
        AppPaths stage,
        CancellationToken cancellationToken)
    {
        var backup = Path.Combine(stage.BackupDirectory, $"migration-{_clock.UtcNow:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(backup);
        if (File.Exists(source.DataFile))
        {
            File.Copy(source.DataFile, Path.Combine(backup, "tasks.json"), overwrite: false);
        }

        if (File.Exists(source.SettingsFile))
        {
            File.Copy(source.SettingsFile, Path.Combine(backup, "settings.json"), overwrite: false);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task ValidateCopiedFilesAsync(
        string sourceRoot,
        string stageRoot,
        CancellationToken cancellationToken)
    {
        foreach (var sourceFile in EnumerateOwnedFiles(sourceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, sourceFile.FullName);
            var targetFile = Path.Combine(stageRoot, relative);
            if (!File.Exists(targetFile) || sourceFile.Length != new FileInfo(targetFile).Length)
            {
                throw new IOException($"迁移文件校验失败：{relative}");
            }

            var sourceHash = await ComputeHashAsync(sourceFile.FullName, cancellationToken).ConfigureAwait(false);
            var targetHash = await ComputeHashAsync(targetFile, cancellationToken).ConfigureAwait(false);
            if (!sourceHash.AsSpan().SequenceEqual(targetHash))
            {
                throw new IOException($"迁移文件哈希不一致：{relative}");
            }
        }
    }

    private async Task ValidateStoreAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        await ValidateSourceOwnershipAsync(paths, cancellationToken).ConfigureAwait(false);
        if (File.Exists(paths.DataFile))
        {
            var repository = new TaskRepository(paths, _clock);
            _ = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(paths.SettingsFile))
        {
            _ = await _settingsJson.ReadAsync(paths.SettingsFile, cancellationToken).ConfigureAwait(false);
        }
    }

    private void CommitStage(string stageRoot, string targetRoot)
    {
        if (PathsEqual(targetRoot, _locationService.BootstrapDirectory))
        {
            Directory.CreateDirectory(targetRoot);
            foreach (var item in new DirectoryInfo(stageRoot).EnumerateFileSystemInfos())
            {
                var destination = Path.Combine(targetRoot, item.Name);
                if (item is DirectoryInfo)
                {
                    Directory.Move(item.FullName, destination);
                }
                else
                {
                    File.Move(item.FullName, destination);
                }
            }

            Directory.Delete(stageRoot, recursive: false);
            return;
        }

        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: false);
        }

        Directory.Move(stageRoot, targetRoot);
    }

    private static void DeleteOwnedContent(string root, bool preserveLocator)
    {
        foreach (var directoryName in OwnedDirectories)
        {
            var directory = Path.Combine(root, directoryName);
            if (Directory.Exists(directory))
            {
                DeleteDirectorySafely(new DirectoryInfo(directory));
            }
        }

        foreach (var fileName in OwnedFiles)
        {
            var file = Path.Combine(root, fileName);
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        if (!preserveLocator && Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root, recursive: false);
        }
    }

    private static void TryDeleteCreatedStore(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                EnsureNotReparsePoint(root);
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A partial target is never activated; leaving it is safer than broad cleanup.
        }
    }

    private static IEnumerable<FileInfo> EnumerateOwnedFiles(string root)
    {
        foreach (var directoryName in OwnedDirectories)
        {
            var directory = Path.Combine(root, directoryName);
            if (Directory.Exists(directory))
            {
                EnsureNotReparsePoint(directory);
                foreach (var file in EnumerateSafeFiles(new DirectoryInfo(directory)))
                {
                    yield return file;
                }
            }
        }

        foreach (var fileName in OwnedFiles)
        {
            var file = Path.Combine(root, fileName);
            if (File.Exists(file))
            {
                yield return new FileInfo(file);
            }
        }
    }

    private static async Task<byte[]> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<FileInfo> EnumerateSafeFiles(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DocumentValidationException("数据目录包含符号链接或目录联接，已拒绝迁移。");
        }

        foreach (var file in directory.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DocumentValidationException("数据目录包含链接文件，已拒绝迁移。");
            }

            yield return file;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            foreach (var file in EnumerateSafeFiles(child))
            {
                yield return file;
            }
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DocumentValidationException("检测到符号链接或目录联接，已拒绝清理。");
        }
    }

    private static void DeleteDirectorySafely(DirectoryInfo directory)
    {
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new DocumentValidationException("检测到符号链接或目录联接，已拒绝清理。");
        }

        foreach (var file in directory.EnumerateFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DocumentValidationException("检测到链接文件，已拒绝清理。");
            }

            file.Delete();
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            DeleteDirectorySafely(child);
        }

        directory.Delete();
    }

    private static IEnumerable<string> GetProtectedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChild(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
        {
            return false;
        }

        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return PathsEqual(normalizedCandidate, normalizedParent)
            || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
