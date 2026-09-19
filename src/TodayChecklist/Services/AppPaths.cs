namespace TodayChecklist.Services;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory, Guid? storeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        StoreId = storeId ?? Guid.Empty;
    }

    public string RootDirectory { get; }

    public Guid StoreId { get; }

    public string DataDirectory => Path.Combine(RootDirectory, "data");

    public string BackupDirectory => Path.Combine(RootDirectory, "backups");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public string DataFile => Path.Combine(DataDirectory, "tasks.json");

    public string LastGoodDataFile => Path.Combine(DataDirectory, "tasks.last-good.json");

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string MarkerFile => Path.Combine(RootDirectory, ".todaychecklist-store.json");

}
