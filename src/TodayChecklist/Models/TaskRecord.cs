namespace TodayChecklist.Models;

public sealed class TaskRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateOnly ScheduledDate { get; set; }

    public DateOnly OriginalDate { get; set; }

    public bool IsCompleted { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public int SortOrder { get; set; }

    public DayPeriod Period { get; set; } = DayPeriod.Morning;

    public Guid? RecurrenceId { get; set; }

    public int RolloverDays { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
