using TodayChecklist.Models;

namespace TodayChecklist.Services;

public sealed class DataLocationService : IDataLocationService
{
    private readonly AtomicJsonFile<StorageLocator> _locatorJson = new(StorageValidation.Validate);
    private readonly AtomicJsonFile<StorageMarker> _markerJson = new(StorageValidation.Validate);

    public DataLocationService(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        BootstrapDirectory = Path.GetFullPath(Path.Combine(localApplicationData, "TodayChecklist"));
        DefaultDataRoot = BootstrapDirectory;
    }

    public string BootstrapDirectory { get; }

    public string DefaultDataRoot { get; }

    public StorageLocator Current { get; private set; } = new();

    private string LocatorFile => Path.Combine(BootstrapDirectory, "location.json");

    private string LastGoodLocatorFile => Path.Combine(BootstrapDirectory, "location.last-good.json");

    public async Task<AppPaths> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(LocatorFile) && !File.Exists(LastGoodLocatorFile))
        {
            return await InitializeDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        var locator = await ReadLocatorWithRecoveryAsync(cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(locator.DataRoot);
        if (!Directory.Exists(root))
        {
            throw new StorageLocationException(
                StorageLocationFailure.Unavailable,
                root,
                $"数据位置当前不可用：{root}");
        }

        var paths = new AppPaths(root, locator.StoreId);
        await ValidateMarkerAsync(paths, cancellationToken).ConfigureAwait(false);
        Current = locator;
        return paths;
    }

    public async Task ActivateAsync(string dataRoot, Guid storeId, CancellationToken cancellationToken = default)
    {
        var locator = new StorageLocator
        {
            DataRoot = Path.GetFullPath(dataRoot),
            StoreId = storeId,
        };
        StorageValidation.Validate(locator);
        var previous = Current.StoreId == Guid.Empty ? null : Current;
        await _locatorJson.WriteAsync(LastGoodLocatorFile, locator, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _locatorJson.WriteAsync(LocatorFile, locator, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (previous is not null)
            {
                await _locatorJson.WriteAsync(LastGoodLocatorFile, previous, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            throw;
        }

        Current = locator;
    }

    private async Task<AppPaths> InitializeDefaultAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DefaultDataRoot);
        var storeId = Guid.NewGuid();
        var paths = new AppPaths(DefaultDataRoot, storeId);

        if (File.Exists(paths.MarkerFile))
        {
            try
            {
                var marker = await _markerJson.ReadAsync(paths.MarkerFile, cancellationToken).ConfigureAwait(false);
                storeId = marker.StoreId;
                paths = new AppPaths(DefaultDataRoot, storeId);
            }
            catch (Exception exception) when (exception is IOException or JsonException or DocumentValidationException)
            {
                throw new StorageLocationException(
                    StorageLocationFailure.InvalidStore,
                    paths.MarkerFile,
                    "默认数据目录的所有权标记无效，已拒绝写入。",
                    exception);
            }
        }
        else
        {
            await _markerJson.WriteAsync(
                paths.MarkerFile,
                new StorageMarker { StoreId = storeId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await ActivateAsync(paths.RootDirectory, storeId, cancellationToken).ConfigureAwait(false);
        return paths;
    }

    private async Task<StorageLocator> ReadLocatorWithRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _locatorJson.ReadAsync(LocatorFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception primaryFailure) when (primaryFailure is IOException or JsonException or DocumentValidationException)
        {
            if (!File.Exists(LastGoodLocatorFile))
            {
                throw new StorageLocationException(
                    StorageLocationFailure.InvalidLocator,
                    LocatorFile,
                    "数据位置配置损坏，且没有可用的安全副本。",
                    primaryFailure);
            }

            try
            {
                return await _locatorJson.ReadAsync(LastGoodLocatorFile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception recoveryFailure) when (recoveryFailure is IOException or JsonException or DocumentValidationException)
            {
                throw new StorageLocationException(
                    StorageLocationFailure.InvalidLocator,
                    LocatorFile,
                    "数据位置配置及其安全副本都无法验证。",
                    recoveryFailure);
            }
        }
    }

    private async Task ValidateMarkerAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        if (!File.Exists(paths.MarkerFile))
        {
            throw new StorageLocationException(
                StorageLocationFailure.InvalidStore,
                paths.RootDirectory,
                "数据目录缺少本程序的所有权标记，已拒绝写入。");
        }

        try
        {
            var marker = await _markerJson.ReadAsync(paths.MarkerFile, cancellationToken).ConfigureAwait(false);
            if (marker.StoreId != paths.StoreId)
            {
                throw new DocumentValidationException("数据目录标识与路径配置不一致。");
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or DocumentValidationException)
        {
            throw new StorageLocationException(
                StorageLocationFailure.InvalidStore,
                paths.RootDirectory,
                "数据目录所有权标记无效，已拒绝写入。",
                exception);
        }
    }
}
