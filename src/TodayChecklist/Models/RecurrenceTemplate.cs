namespace TodayChecklist.Models;

public sealed class RecurrenceTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public RepeatKind Kind { get; set; }

    public DateOnly StartDate { get; set; }

    public DayOfWeek? Weekday { get; set; }

    public int? DayOfMonth { get; set; }

    public bool IsActive { get; set; } = true;

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

