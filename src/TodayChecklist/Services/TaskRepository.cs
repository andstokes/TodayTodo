using TodayChecklist.Models;

namespace TodayChecklist.Services;

public sealed class TaskRepository : ITaskRepository
{
    private const int DailyBackupRetention = 14;
    private readonly AppPaths _paths;
    private readonly IClock _clock;
    private readonly AtomicJsonFile<TaskDataDocument> _json = new(DocumentValidator.Validate);

    public TaskRepository(AppPaths paths, IClock clock)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<TaskDataDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.DataFile))
        {
            return new TaskDataDocument();
        }

        try
        {
            return await _json.ReadAsync(_paths.DataFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception primaryFailure) when (primaryFailure is IOException or JsonException or DocumentValidationException)
        {
            if (!File.Exists(_paths.LastGoodDataFile))
            {
                throw new DataRecoveryException(_paths.DataFile, primaryFailure, null);
            }

            try
            {
                return await _json.ReadAsync(_paths.LastGoodDataFile, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception recoveryFailure) when (recoveryFailure is IOException or JsonException or DocumentValidationException)
            {
                throw new DataRecoveryException(_paths.DataFile, primaryFailure, recoveryFailure);
            }
        }
    }

    public async Task SaveAsync(TaskDataDocument document, CancellationToken cancellationToken = default)
    {
        DocumentValidator.Validate(document);
        CreateDailyBackupIfNeeded();
        await _json.WriteAsync(_paths.DataFile, document, _paths.LastGoodDataFile, cancellationToken)
            .ConfigureAwait(false);
        RotateDailyBackups();
    }

    public Task ExportAsync(
        TaskDataDocument document,
        string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return _json.WriteAsync(Path.GetFullPath(destination), document, cancellationToken: cancellationToken);
    }

    public async Task<TaskDataDocument> ImportAsync(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var candidate = await _json.ReadAsync(Path.GetFullPath(source), cancellationToken).ConfigureAwait(false);

        if (File.Exists(_paths.DataFile))
        {
            Directory.CreateDirectory(_paths.BackupDirectory);
            var backupName = $"tasks-before-import-{_clock.UtcNow:yyyyMMdd-HHmmss}.json";
            File.Copy(_paths.DataFile, Path.Combine(_paths.BackupDirectory, backupName), overwrite: false);
        }

        await SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        return candidate;
    }

    private void CreateDailyBackupIfNeeded()
    {
        if (!File.Exists(_paths.DataFile))
        {
            return;
        }

        Directory.CreateDirectory(_paths.BackupDirectory);
        var backupPath = Path.Combine(_paths.BackupDirectory, $"tasks-{_clock.Today:yyyy-MM-dd}.json");
        if (!File.Exists(backupPath))
        {
            File.Copy(_paths.DataFile, backupPath, overwrite: false);
        }
    }

    private void RotateDailyBackups()
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            return;
        }

        var backups = new DirectoryInfo(_paths.BackupDirectory)
            .EnumerateFiles("tasks-????-??-??.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.Name, StringComparer.Ordinal)
            .Skip(DailyBackupRetention)
            .ToArray();

        foreach (var backup in backups)
        {
            backup.Delete();
        }
    }
}

