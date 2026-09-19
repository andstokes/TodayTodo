using TodayChecklist.Models;

namespace TodayChecklist.ViewModels;

public sealed class TaskItemViewModel : ScheduleRowViewModel
{
    private readonly bool _showRolloverDays;

    public TaskItemViewModel(TaskRecord task, bool showRolloverDays = true)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        _showRolloverDays = showRolloverDays;
    }

    public TaskRecord Task { get; }

    public Guid Id => Task.Id;

    public override string RowKey => Id.ToString("D", CultureInfo.InvariantCulture);

    public override bool CanDrag => !IsCompleted;

    public string Title => Task.Title;

    public string Notes => Task.Notes;

    public bool HasNotes => !string.IsNullOrWhiteSpace(Task.Notes);

    public bool IsCompleted => Task.IsCompleted;

    public bool HasRolledOver => _showRolloverDays && Task.RolloverDays > 0;

    public string RolloverText => Task.RolloverDays > 0 ? $"已顺延 {Task.RolloverDays} 天" : string.Empty;

    public bool IsRecurring => Task.RecurrenceId is not null;

}
