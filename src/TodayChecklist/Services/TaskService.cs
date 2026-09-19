using TodayChecklist.Models;

namespace TodayChecklist.Services;

public sealed class TaskService
{
    private readonly IClock _clock;

    public TaskService(IClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public TaskRecord AddTask(
        TaskDataDocument document,
        string title,
        string notes,
        DateOnly date,
        RepeatKind repeatKind)
    {
        ArgumentNullException.ThrowIfNull(document);
        title = NormalizeTitle(title);
        notes = NormalizeNotes(notes);

        Guid? recurrenceId = null;
        if (repeatKind != RepeatKind.None)
        {
            var recurrence = new RecurrenceTemplate
            {
                Kind = repeatKind,
                StartDate = date,
                Weekday = repeatKind == RepeatKind.Weekly ? date.DayOfWeek : null,
                DayOfMonth = repeatKind == RepeatKind.Monthly ? date.Day : null,
                Title = title,
                Notes = notes,
                CreatedAtUtc = _clock.UtcNow,
                UpdatedAtUtc = _clock.UtcNow,
            };
            document.Recurrences.Add(recurrence);
            recurrenceId = recurrence.Id;
        }

        var task = CreateTask(document, title, notes, date, recurrenceId);
        document.Tasks.Add(task);
        DocumentValidator.Validate(document);
        return task;
    }

    public bool PrepareForDate(TaskDataDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        var changed = false;

        foreach (var task in document.Tasks.Where(task => !task.IsCompleted && task.ScheduledDate < today))
        {
            task.RolloverDays += today.DayNumber - task.ScheduledDate.DayNumber;
            task.ScheduledDate = today;
            task.UpdatedAtUtc = _clock.UtcNow;
            changed = true;
        }

        foreach (var recurrence in document.Recurrences.Where(static recurrence => recurrence.IsActive))
        {
            if (!IsDue(recurrence, today))
            {
                continue;
            }

            var hasCurrentOrIncomplete = document.Tasks.Any(task =>
                task.RecurrenceId == recurrence.Id
                && (!task.IsCompleted || task.ScheduledDate == today));
            if (hasCurrentOrIncomplete)
            {
                continue;
            }

            document.Tasks.Add(CreateTask(document, recurrence.Title, recurrence.Notes, today, recurrence.Id));
            changed = true;
        }

        if (changed)
        {
            DocumentValidator.Validate(document);
        }

        return changed;
    }

    public void SetCompleted(TaskRecord task, bool completed)
    {
        ArgumentNullException.ThrowIfNull(task);
        task.IsCompleted = completed;
        task.CompletedAtUtc = completed ? _clock.UtcNow : null;
        task.UpdatedAtUtc = _clock.UtcNow;
    }

    public void EditTask(TaskDataDocument document, TaskRecord task, string title, string notes, bool applyToSeries)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(task);
        title = NormalizeTitle(title);
        notes = NormalizeNotes(notes);

        task.Title = title;
        task.Notes = notes;
        task.UpdatedAtUtc = _clock.UtcNow;

        if (applyToSeries && task.RecurrenceId is Guid recurrenceId)
        {
            var recurrence = document.Recurrences.Single(item => item.Id == recurrenceId);
            recurrence.Title = title;
            recurrence.Notes = notes;
            recurrence.UpdatedAtUtc = _clock.UtcNow;
        }

        DocumentValidator.Validate(document);
    }

    public TaskRecord DeleteTask(TaskDataDocument document, Guid taskId, bool stopRecurrence)
    {
        ArgumentNullException.ThrowIfNull(document);
        var task = document.Tasks.Single(item => item.Id == taskId);
        _ = document.Tasks.Remove(task);

        if (stopRecurrence && task.RecurrenceId is Guid recurrenceId)
        {
            var recurrence = document.Recurrences.Single(item => item.Id == recurrenceId);
            recurrence.IsActive = false;
            recurrence.UpdatedAtUtc = _clock.UtcNow;
        }

        return task;
    }

    public static void RestoreDeletedTask(TaskDataDocument document, TaskRecord task)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(task);
        if (document.Tasks.Any(item => item.Id == task.Id))
        {
            return;
        }

        document.Tasks.Add(task);
        DocumentValidator.Validate(document);
    }

    public void Reorder(TaskDataDocument document, Guid taskId, int targetIndex, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ordered = document.Tasks
            .Where(task => task.ScheduledDate == date && !task.IsCompleted)
            .OrderBy(task => task.Period)
            .ThenBy(task => task.SortOrder)
            .ThenBy(task => task.CreatedAtUtc)
            .ToList();
        var source = ordered.SingleOrDefault(task => task.Id == taskId);
        if (source is null)
        {
            return;
        }

        _ = ordered.Remove(source);
        if (targetIndex < 0 || targetIndex > ordered.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetIndex));
        }

        ordered.Insert(targetIndex, source);
        // Flat-view moves follow their neighbours so switching the dividers back on
        // does not undo the user's latest ordering.
        source.Period = targetIndex > 0 ? ordered[targetIndex - 1].Period
            : ordered.Count > 1 ? ordered[1].Period : source.Period;
        source.UpdatedAtUtc = _clock.UtcNow;
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index].SortOrder != index)
            {
                ordered[index].SortOrder = index;
                ordered[index].UpdatedAtUtc = _clock.UtcNow;
            }
        }
    }

    public static bool IsDue(RecurrenceTemplate recurrence, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        if (!recurrence.IsActive || date < recurrence.StartDate)
        {
            return false;
        }

        return recurrence.Kind switch
        {
            RepeatKind.Daily => true,
            RepeatKind.Weekdays => date.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday,
            RepeatKind.Weekly => date.DayOfWeek == recurrence.Weekday,
            RepeatKind.Monthly => date.Day == Math.Min(
                recurrence.DayOfMonth ?? recurrence.StartDate.Day,
                DateTime.DaysInMonth(date.Year, date.Month)),
            _ => false,
        };
    }

    private TaskRecord CreateTask(
        TaskDataDocument document,
        string title,
        string notes,
        DateOnly date,
        Guid? recurrenceId)
    {
        var nextOrder = document.Tasks
            .Where(task => task.ScheduledDate == date && !task.IsCompleted)
            .Select(static task => task.SortOrder)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        return new TaskRecord
        {
            Title = title,
            Notes = notes,
            ScheduledDate = date,
            OriginalDate = date,
            SortOrder = nextOrder,
            RecurrenceId = recurrenceId,
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow,
        };
    }

    private static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var value = title.Trim();
        if (value.Length is < 1 or > 200)
        {
            throw new DocumentValidationException("任务标题必须为 1 到 200 个字符。");
        }

        return value;
    }

    private static string NormalizeNotes(string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var value = notes.Trim();
        if (value.Length > 2_000)
        {
            throw new DocumentValidationException("任务备注不能超过 2,000 个字符。");
        }

        return value;
    }
}
