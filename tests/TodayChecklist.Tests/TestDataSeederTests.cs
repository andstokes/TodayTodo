using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class TestDataSeederTests
{
    [TestMethod]
    public void SeedIfEmpty_CreatesCurrentAndHistoricalSyntheticTasks()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)));
        var taskService = new TaskService(clock);
        var document = new TaskDataDocument();

        var changed = TestDataSeeder.SeedIfEmpty(document, taskService, clock.Today);
        var history = new HistoryViewModel(document, clock.Today);

        Assert.IsTrue(changed);
        Assert.AreEqual(5, document.Tasks.Count);
        Assert.AreEqual(2, history.DayCount);
        Assert.AreEqual(3, document.Tasks.Count(task => task.ScheduledDate == clock.Today));
    }

    [TestMethod]
    public void SeedIfEmpty_DoesNotAlterExistingDocument()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.FromHours(8)));
        var taskService = new TaskService(clock);
        var document = new TaskDataDocument();
        var existing = taskService.AddTask(document, "existing", string.Empty, clock.Today, RepeatKind.None);

        var changed = TestDataSeeder.SeedIfEmpty(document, taskService, clock.Today);

        Assert.IsFalse(changed);
        Assert.AreEqual(1, document.Tasks.Count);
        Assert.AreSame(existing, document.Tasks.Single());
    }
}
