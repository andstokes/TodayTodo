namespace TodayChecklist.Models;

public sealed class TaskDataDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<TaskRecord> Tasks { get; set; } = [];

    public List<RecurrenceTemplate> Recurrences { get; set; } = [];
}

