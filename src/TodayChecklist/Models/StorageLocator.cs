namespace TodayChecklist.Models;

public sealed class StorageLocator
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string DataRoot { get; set; } = string.Empty;

    public Guid StoreId { get; set; }
}

public sealed class StorageMarker
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid StoreId { get; set; }
}

