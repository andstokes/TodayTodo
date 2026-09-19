using Microsoft.VisualStudio.TestTools.UnitTesting;
using TodayChecklist.Models;
using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class TaskServiceTests
{
    [TestMethod]
    public void AddTask_RejectsBlankTitle()
    {
        var service = CreateService();
        var document = new TaskDataDocument();

        _ = Assert.ThrowsExactly<DocumentValidationException>(() =>
            service.AddTask(document, "   ", string.Empty, new DateOnly(2026, 8, 31), RepeatKind.None));
    }

    [TestMethod]
    public void PrepareForDate_RollsIncompleteTaskAndPreservesOriginalDate()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var original = new DateOnly(2026, 8, 31);
        var task = service.AddTask(document, "整理桌面", string.Empty, original, RepeatKind.None);

        var changed = service.PrepareForDate(document, clock.Today);

        Assert.IsTrue(changed);
        Assert.AreEqual(clock.Today, task.ScheduledDate);
        Assert.AreEqual(original, task.OriginalDate);
        Assert.AreEqual(3, task.RolloverDays);
    }

    [TestMethod]
    public void PrepareForDate_DoesNotRollCompletedTask()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var original = new DateOnly(2026, 8, 31);
        var task = service.AddTask(document, "已完成", string.Empty, original, RepeatKind.None);
        service.SetCompleted(task, completed: true);

        _ = service.PrepareForDate(document, clock.Today);

        Assert.AreEqual(original, task.ScheduledDate);
    }

    [TestMethod]
    public void PrepareForDate_RecurringTaskKeepsSingleIncompleteOccurrence()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var task = service.AddTask(
            document,
            "喝水",
            string.Empty,
            new DateOnly(2026, 8, 31),
            RepeatKind.Daily);

        _ = service.PrepareForDate(document, clock.Today);

        Assert.AreEqual(1, document.Tasks.Count);
        Assert.AreEqual(clock.Today, task.ScheduledDate);
    }

    [TestMethod]
    public void PrepareForDate_GeneratesNextOccurrenceAfterCompletion()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var task = service.AddTask(document, "喝水", string.Empty, clock.Today, RepeatKind.Daily);
        service.SetCompleted(task, completed: true);
        clock.Set(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));

        var changed = service.PrepareForDate(document, clock.Today);

        Assert.IsTrue(changed);
        Assert.AreEqual(2, document.Tasks.Count);
        var current = document.Tasks.Single(item => !item.IsCompleted);
        Assert.AreEqual(clock.Today, current.ScheduledDate);
        Assert.AreEqual(task.RecurrenceId, current.RecurrenceId);
    }

    [TestMethod]
    public void IsDue_MonthlyDayClampsToMonthEnd()
    {
        var recurrence = new RecurrenceTemplate
        {
            Kind = RepeatKind.Monthly,
            StartDate = new DateOnly(2026, 1, 31),
            DayOfMonth = 31,
            Title = "月末检查",
        };

        Assert.IsTrue(TaskService.IsDue(recurrence, new DateOnly(2026, 2, 28)));
        Assert.IsFalse(TaskService.IsDue(recurrence, new DateOnly(2026, 2, 27)));
    }

    [TestMethod]
    public void DeleteAndRestore_RestoresSameTask()
    {
        var service = CreateService();
        var document = new TaskDataDocument();
        var task = service.AddTask(document, "可撤销", "备注", new DateOnly(2026, 8, 31), RepeatKind.None);

        var deleted = service.DeleteTask(document, task.Id, stopRecurrence: false);
        TaskService.RestoreDeletedTask(document, deleted);

        Assert.AreEqual(1, document.Tasks.Count);
        Assert.AreSame(task, document.Tasks[0]);
    }

    private static TaskService CreateService()
    {
        return new TaskService(new TestClock(
            new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8))));
    }
}
