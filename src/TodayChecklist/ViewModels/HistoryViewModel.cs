using TodayChecklist.Models;

namespace TodayChecklist.ViewModels;

public sealed class HistoryViewModel
{
    public HistoryViewModel(TaskDataDocument document, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);

        Days = document.Tasks
            .Where(task => task.IsCompleted && task.ScheduledDate < today)
            .GroupBy(static task => task.ScheduledDate)
            .OrderByDescending(static group => group.Key)
            .Select(static group => new HistoryDayViewModel(
                group.Key,
                group
                    .OrderBy(static task => task.SortOrder)
                    .ThenBy(static task => task.CompletedAtUtc)
                    .ThenBy(static task => task.CreatedAtUtc)
                    .Select(static task => new HistoryTaskItemViewModel(task.Title, task.Notes))
                    .ToArray()))
            .ToArray();
    }

    public IReadOnlyList<HistoryDayViewModel> Days { get; }

    public int DayCount => Days.Count;
}

public sealed class HistoryDayViewModel
{
    public HistoryDayViewModel(DateOnly date, IReadOnlyList<HistoryTaskItemViewModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Date = date;
        Items = items;
    }

    public DateOnly Date { get; }

    public string DateText => Date.ToDateTime(TimeOnly.MinValue)
        .ToString("yyyy年M月d日 dddd", CultureInfo.CurrentCulture);

    public IReadOnlyList<HistoryTaskItemViewModel> Items { get; }

    public string CompletedCountText => string.Create(
        CultureInfo.CurrentCulture,
        $"完成 {Items.Count} 项");
}

public sealed class HistoryTaskItemViewModel
{
    public HistoryTaskItemViewModel(string title, string notes)
    {
        Title = title;
        Notes = notes;
    }

    public string Title { get; }

    public string Notes { get; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
}
