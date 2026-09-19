using TodayChecklist.Models;
using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class TaskReorderTests
{
    private static readonly string[] LastToFirstTitles = ["C", "A", "B"];
    private static readonly string[] FirstToLastTitles = ["B", "C", "A"];
    private static readonly int[] SequentialOrders = [0, 1, 2];

    [TestMethod]
    public void Reorder_MovesLastTaskToFirstAndNormalizesOrders()
    {
        var (service, document, date) = CreateThreeTasks();
        var last = document.Tasks.Single(task => task.Title == "C");

        service.Reorder(document, last.Id, 0, date);

        CollectionAssert.AreEqual(
            LastToFirstTitles,
            document.Tasks.OrderBy(task => task.SortOrder).Select(task => task.Title).ToArray());
        CollectionAssert.AreEqual(
            SequentialOrders,
            document.Tasks.OrderBy(task => task.SortOrder).Select(task => task.SortOrder).ToArray());
    }

    [TestMethod]
    public void Reorder_MovesFirstTaskToLast()
    {
        var (service, document, date) = CreateThreeTasks();
        var first = document.Tasks.Single(task => task.Title == "A");

        service.Reorder(document, first.Id, 2, date);

        CollectionAssert.AreEqual(
            FirstToLastTitles,
            document.Tasks.OrderBy(task => task.SortOrder).Select(task => task.Title).ToArray());
    }

    [TestMethod]
    public void Reorder_RejectsIndexOutsideRemainingItems()
    {
        var (service, document, date) = CreateThreeTasks();
        var task = document.Tasks[0];

        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            service.Reorder(document, task.Id, 3, date));

        CollectionAssert.AreEqual(SequentialOrders, document.Tasks.Select(item => item.SortOrder).ToArray());
    }

    [TestMethod]
    public void Reorder_DoesNotMoveCompletedTask()
    {
        var (service, document, date) = CreateThreeTasks();
        var completed = document.Tasks[1];
        service.SetCompleted(completed, completed: true);
        var orders = document.Tasks.Select(task => task.SortOrder).ToArray();

        service.Reorder(document, completed.Id, 0, date);

        CollectionAssert.AreEqual(orders, document.Tasks.Select(task => task.SortOrder).ToArray());
    }

    private static (TaskService Service, TaskDataDocument Document, DateOnly Date) CreateThreeTasks()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "A", string.Empty, clock.Today, RepeatKind.None);
        _ = service.AddTask(document, "B", string.Empty, clock.Today, RepeatKind.None);
        _ = service.AddTask(document, "C", string.Empty, clock.Today, RepeatKind.None);
        return (service, document, clock.Today);
    }
}
