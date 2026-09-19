namespace TodayChecklist.Services;

public interface IAppLogger
{
    void Info(string eventCode);

    void LogError(string eventCode, Exception exception);
}

public interface IRelocatableAppLogger : IAppLogger
{
    void Relocate(AppPaths paths);
}

public sealed class FileAppLogger : IRelocatableAppLogger
{
    private const int RetentionDays = 7;
    private AppPaths _paths;
    private readonly object _gate = new();

    public FileAppLogger(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void Info(string eventCode) => Write("INFO", eventCode, null);

    public void LogError(string eventCode, Exception exception) => Write("ERROR", eventCode, exception);

    public void Relocate(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            _paths = paths;
        }
    }

    private void Write(string level, string eventCode, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.LogDirectory);
                var path = Path.Combine(_paths.LogDirectory, $"app-{DateTime.UtcNow:yyyy-MM-dd}.log");
                var exceptionName = exception is null ? string.Empty : $" | {exception.GetType().Name}";
                File.AppendAllText(path, $"{DateTime.UtcNow:O} | {level} | {eventCode}{exceptionName}{Environment.NewLine}");
                Rotate();
            }
        }
        catch
        {
            // Logging must never make the application unusable.
        }
    }

    private void Rotate()
    {
        var threshold = DateTime.UtcNow.Date.AddDays(-RetentionDays);
        foreach (var file in new DirectoryInfo(_paths.LogDirectory).EnumerateFiles("app-*.log"))
        {
            if (file.LastWriteTimeUtc < threshold)
            {
                file.Delete();
            }
        }
    }
}
