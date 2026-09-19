using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class MainViewModelTests
{
    private static readonly string[] OriginalTaskOrder = ["A", "B", "C"];

    [TestMethod]
    public async Task UndoDelete_ReactivatesStoppedRecurrence()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var task = service.AddTask(document, "每天复盘", string.Empty, clock.Today, RepeatKind.Daily);
        var repository = new InMemoryTaskRepository();
        var viewModel = new MainViewModel(document, repository, service, clock, new NoOpLogger());

        await viewModel.DeleteAsync(task.Id, stopRecurrence: true);
        Assert.IsFalse(document.Recurrences.Single().IsActive);

        await viewModel.UndoDeleteAsync();

        Assert.IsTrue(document.Recurrences.Single().IsActive);
        Assert.AreEqual(1, document.Tasks.Count);
        Assert.AreEqual(2, repository.SaveCount);
    }

    [TestMethod]
    public async Task FailedSave_RollsBackInMemoryMutation()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        var task = service.AddTask(document, "不能丢失", string.Empty, clock.Today, RepeatKind.None);
        var repository = new InMemoryTaskRepository { FailSaves = true };
        var viewModel = new MainViewModel(document, repository, service, clock, new NoOpLogger());

        await Assert.ThrowsAsync<IOException>(() => viewModel.SetCompletedAsync(task.Id, completed: true));

        Assert.IsFalse(viewModel.Tasks.Single().IsCompleted);
    }

    [TestMethod]
    public async Task FailedReorderSave_RestoresOriginalOrder()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "A", string.Empty, clock.Today, RepeatKind.None);
        _ = service.AddTask(document, "B", string.Empty, clock.Today, RepeatKind.None);
        var last = service.AddTask(document, "C", string.Empty, clock.Today, RepeatKind.None);
        var repository = new InMemoryTaskRepository { FailSaves = true };
        var viewModel = new MainViewModel(document, repository, service, clock, new NoOpLogger());

        await Assert.ThrowsAsync<IOException>(() => viewModel.ReorderAsync(last.Id, 0));

        CollectionAssert.AreEqual(
            OriginalTaskOrder,
            viewModel.Tasks.Select(item => item.Title).ToArray());
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public int SaveCount { get; private set; }

        public bool FailSaves { get; init; }

        public Task<TaskDataDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TaskDataDocument());
        }

        public Task SaveAsync(TaskDataDocument document, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return FailSaves
                ? Task.FromException(new IOException("simulated"))
                : Task.CompletedTask;
        }

        public Task ExportAsync(TaskDataDocument document, string destination, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<TaskDataDocument> ImportAsync(string source, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TaskDataDocument());
        }
    }

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string eventCode)
        {
        }

        public void LogError(string eventCode, Exception exception)
        {
        }
    }
}
