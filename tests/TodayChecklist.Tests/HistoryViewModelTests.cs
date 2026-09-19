using Microsoft.VisualStudio.TestTools.UnitTesting;
using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class HistoryViewModelTests
{
    [TestMethod]
    public void Constructor_GroupsOnlyEarlierCompletedTasksByNewestDate()
    {
        var today = new DateOnly(2026, 9, 7);
        var document = new TaskDataDocument
        {
            Tasks =
            [
                CreateTask("较早完成", new DateOnly(2026, 9, 5), completed: true, sortOrder: 0),
                CreateTask("昨天第二项", new DateOnly(2026, 9, 6), completed: true, sortOrder: 2),
                CreateTask("昨天第一项", new DateOnly(2026, 9, 6), completed: true, sortOrder: 1),
                CreateTask("今天完成", today, completed: true, sortOrder: 0),
                CreateTask("过去未完成", new DateOnly(2026, 9, 4), completed: false, sortOrder: 0),
            ],
        };

        var viewModel = new HistoryViewModel(document, today);

        Assert.AreEqual(2, viewModel.DayCount);
        Assert.AreEqual(new DateOnly(2026, 9, 6), viewModel.Days[0].Date);
        Assert.AreEqual(2, viewModel.Days[0].Items.Count);
        Assert.AreEqual("昨天第一项", viewModel.Days[0].Items[0].Title);
        Assert.AreEqual("昨天第二项", viewModel.Days[0].Items[1].Title);
        Assert.AreEqual(new DateOnly(2026, 9, 5), viewModel.Days[1].Date);
    }

    [TestMethod]
    public void Constructor_PreservesNotesWithoutChangingDocument()
    {
        var date = new DateOnly(2026, 9, 6);
        var source = CreateTask("已完成", date, completed: true, sortOrder: 0);
        source.Notes = "只读历史备注";
        var document = new TaskDataDocument { Tasks = [source] };

        var viewModel = new HistoryViewModel(document, new DateOnly(2026, 9, 7));

        Assert.AreEqual("只读历史备注", viewModel.Days.Single().Items.Single().Notes);
        Assert.AreSame(source, document.Tasks.Single());
    }

    [TestMethod]
    public void DateRollover_MakesCompletedTaskAvailableToHistory()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var task = service.AddTask(document, "完成后归档", string.Empty, clock.Today, RepeatKind.None);
        service.SetCompleted(task, completed: true);

        clock.Set(new DateTimeOffset(2026, 9, 7, 0, 1, 0, TimeSpan.FromHours(8)));
        _ = service.PrepareForDate(document, clock.Today);
        var history = new HistoryViewModel(document, clock.Today);

        Assert.AreEqual(1, history.DayCount);
        Assert.AreEqual(new DateOnly(2026, 9, 6), history.Days.Single().Date);
        Assert.AreEqual("完成后归档", history.Days.Single().Items.Single().Title);
    }

    private static TaskRecord CreateTask(string title, DateOnly date, bool completed, int sortOrder)
    {
        return new TaskRecord
        {
            Title = title,
            ScheduledDate = date,
            OriginalDate = date,
            IsCompleted = completed,
            CompletedAtUtc = completed
                ? new DateTimeOffset(date.ToDateTime(new TimeOnly(8, 0)), TimeSpan.FromHours(8)).ToUniversalTime()
                : null,
            SortOrder = sortOrder,
        };
    }
}
