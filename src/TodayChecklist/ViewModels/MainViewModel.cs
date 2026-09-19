using System.Collections.ObjectModel;
using TodayChecklist.Models;
using TodayChecklist.Services;

namespace TodayChecklist.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly TaskService _taskService;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private TaskDataDocument _document;
    private TaskRecord? _lastDeleted;
    private bool _lastDeleteStoppedRecurrence;
    private bool _showRolloverDays = true;

    public MainViewModel(
        TaskDataDocument document,
        ITaskRepository repository,
        TaskService taskService,
        IClock clock,
        IAppLogger logger,
        bool isReadOnly = false)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        IsReadOnly = isReadOnly;
        Refresh();
    }

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<ScheduleRowViewModel> Rows { get; } = [];

    public bool ShowDayDividers { get; private set; }

    public bool ShowEmptyHint => Tasks.Count == 0 && !ShowDayDividers;

    public void ApplyDisplayOptions(bool showRolloverDays, bool showDayDividers)
    {
        _showRolloverDays = showRolloverDays;
        ShowDayDividers = showDayDividers;
        OnPropertyChanged(nameof(ShowDayDividers));
        Refresh();
    }

    public bool CanMoveRow(string sourceKey, string targetKey, bool after)
    {
        return !IsReadOnly && PlanMove(sourceKey, targetKey, after) is not null;
    }

    public Task MoveRowAsync(string sourceKey, string targetKey, bool after)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("当前处于只读恢复模式，不能修改顺序。");
        }

        var rows = PlanMove(sourceKey, targetKey, after);
        if (rows is null)
        {
            return Task.CompletedTask;
        }

        if (!ShowDayDividers)
        {
            var source = (TaskItemViewModel)rows.Single(row => row.RowKey == sourceKey);
            return ReorderAsync(source.Id, rows.IndexOf(source));
        }

        return MutateAndSaveAsync(document =>
        {
            var period = DayPeriod.Unscheduled;
            var order = 0;
            foreach (var row in rows)
            {
                if (row is DayDividerViewModel divider)
                {
                    period = divider.Period!.Value;
                }
                else if (row is TaskItemViewModel item)
                {
                    var task = document.Tasks.Single(task => task.Id == item.Id);
                    if (task.Period != period || task.SortOrder != order)
                    {
                        task.Period = period;
                        task.SortOrder = order;
                        task.UpdatedAtUtc = _clock.UtcNow;
                    }

                    order++;
                }
            }
        });
    }

    private List<ScheduleRowViewModel>? PlanMove(string sourceKey, string targetKey, bool after)
    {
        if (sourceKey == targetKey)
        {
            return null;
        }

        var rows = Rows.Where(static row => row.CanDrag).ToList();
        var source = rows.SingleOrDefault(row => row.RowKey == sourceKey);
        if (source is null)
        {
            return null;
        }

        rows.Remove(source);
        var index = rows.FindIndex(row => row.RowKey == targetKey);
        if (index < 0)
        {
            return null;
        }

        rows.Insert(index + (after ? 1 : 0), source);
        // Reject crossing another divider; ordinary tasks may cross any divider.
        var expected = DayPeriod.Morning;
        foreach (var divider in rows.OfType<DayDividerViewModel>())
        {
            if (divider.Period != expected)
            {
                return null;
            }

            expected++;
        }

        return rows;
    }

    public DateOnly Today => _clock.Today;

    public string DateText => Today.ToDateTime(TimeOnly.MinValue).ToString("M月d日 dddd", CultureInfo.CurrentCulture);

    public string SummaryText
    {
        get
        {
            var completed = Tasks.Count(static item => item.IsCompleted);
            return Tasks.Count == 0 ? "今天还没有任务" : $"已完成 {completed} / {Tasks.Count}";
        }
    }

    public bool IsReadOnly { get; }

    public bool CanUndoDelete => _lastDeleted is not null && !IsReadOnly;

    public async Task AddAsync(string title, string notes, RepeatKind repeatKind)
    {
        await MutateAndSaveAsync(document =>
            _taskService.AddTask(document, title, notes, Today, repeatKind));
    }

    public Task SetCompletedAsync(Guid id, bool completed)
    {
        return MutateAndSaveAsync(document =>
            _taskService.SetCompleted(document.Tasks.Single(task => task.Id == id), completed));
    }

    public Task EditAsync(Guid id, string title, string notes, bool applyToSeries)
    {
        return MutateAndSaveAsync(document =>
        {
            var task = document.Tasks.Single(item => item.Id == id);
            _taskService.EditTask(document, task, title, notes, applyToSeries);
        });
    }

    public async Task DeleteAsync(Guid id, bool stopRecurrence)
    {
        TaskRecord? deleted = null;
        await MutateAndSaveAsync(document =>
        {
            deleted = _taskService.DeleteTask(document, id, stopRecurrence);
        });
        _lastDeleted = deleted;
        _lastDeleteStoppedRecurrence = stopRecurrence;
        OnPropertyChanged(nameof(CanUndoDelete));
    }

    public async Task UndoDeleteAsync()
    {
        if (_lastDeleted is null)
        {
            return;
        }

        var deleted = _lastDeleted;
        await MutateAndSaveAsync(document =>
        {
            TaskService.RestoreDeletedTask(document, deleted);
            if (_lastDeleteStoppedRecurrence && deleted.RecurrenceId is Guid recurrenceId)
            {
                var recurrence = document.Recurrences.Single(item => item.Id == recurrenceId);
                recurrence.IsActive = true;
                recurrence.UpdatedAtUtc = _clock.UtcNow;
            }
        });
        _lastDeleted = null;
        _lastDeleteStoppedRecurrence = false;
        OnPropertyChanged(nameof(CanUndoDelete));
    }

    public Task ReorderAsync(Guid id, int targetIndex)
    {
        return MutateAndSaveAsync(document => _taskService.Reorder(document, id, targetIndex, Today));
    }

    public async Task ReplaceDocumentAsync(TaskDataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (IsReadOnly)
        {
            throw new InvalidOperationException("数据恢复模式下不能导入。");
        }

        _document = document;
        _lastDeleted = null;
        _lastDeleteStoppedRecurrence = false;
        Refresh();
        await Task.CompletedTask;
    }

    public TaskDataDocument GetDocumentSnapshot()
    {
        return Clone(_document);
    }

    public bool RefreshForDate()
    {
        var changed = _taskService.PrepareForDate(_document, Today);
        Refresh();
        return changed;
    }

    public void Refresh()
    {
        var items = _document.Tasks
            .Where(task => task.ScheduledDate == Today)
            .OrderBy(static task => task.IsCompleted)
            .ThenBy(static task => !task.IsCompleted ? task.Period : DayPeriod.Unscheduled)
            .ThenBy(static task => task.SortOrder)
            .ThenBy(static task => task.CreatedAtUtc)
            .Select(task => new TaskItemViewModel(task, _showRolloverDays))
            .ToArray();

        Tasks.Clear();
        foreach (var item in items)
        {
            Tasks.Add(item);
        }

        Rows.Clear();
        if (ShowDayDividers)
        {
            foreach (var period in Enum.GetValues<DayPeriod>())
            {
                if (period != DayPeriod.Unscheduled)
                {
                    Rows.Add(new DayDividerViewModel(period));
                }

                foreach (var item in items.Where(item => !item.IsCompleted && item.Task.Period == period))
                {
                    Rows.Add(item);
                }
            }

            if (items.Any(static item => item.IsCompleted))
            {
                Rows.Add(new DayDividerViewModel(null));
                foreach (var item in items.Where(static item => item.IsCompleted))
                {
                    Rows.Add(item);
                }
            }
        }
        else
        {
            foreach (var item in items)
            {
                Rows.Add(item);
            }
        }

        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(ShowEmptyHint));
    }

    private async Task MutateAndSaveAsync(Action<TaskDataDocument> mutation)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("当前处于只读恢复模式，不能修改任务。");
        }

        var snapshot = Clone(_document);
        try
        {
            mutation(_document);
            await _repository.SaveAsync(_document).ConfigureAwait(true);
            Refresh();
        }
        catch (Exception exception)
        {
            _document = snapshot;
            Refresh();
            _logger.LogError("TASK_MUTATION_ROLLED_BACK", exception);
            throw;
        }
    }

    private static TaskDataDocument Clone(TaskDataDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonDefaults.Options);
        return JsonSerializer.Deserialize<TaskDataDocument>(json, JsonDefaults.Options)
            ?? throw new InvalidOperationException("无法创建数据安全快照。");
    }
}
