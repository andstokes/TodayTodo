using Microsoft.VisualStudio.TestTools.UnitTesting;
using TodayChecklist.Models;
using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class TaskRepositoryTests
{
    [TestMethod]
    public async Task SaveAndLoad_RoundTripsUnicodeContent()
    {
        using var directory = new TestDirectory();
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var repository = new TaskRepository(new AppPaths(directory.Path), clock);
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "整理今日清单", "支持中文备注", clock.Today, RepeatKind.Weekdays);

        await repository.SaveAsync(document);
        var loaded = await repository.LoadAsync();

        Assert.AreEqual(1, loaded.Tasks.Count);
        Assert.AreEqual("整理今日清单", loaded.Tasks[0].Title);
        Assert.AreEqual("支持中文备注", loaded.Tasks[0].Notes);
        Assert.AreEqual(1, loaded.Recurrences.Count);
    }

    [TestMethod]
    public async Task Save_CreatesLastGoodAfterSecondWrite()
    {
        using var directory = new TestDirectory();
        var paths = new AppPaths(directory.Path);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var repository = new TaskRepository(paths, clock);
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "第一版", string.Empty, clock.Today, RepeatKind.None);
        await repository.SaveAsync(document);
        _ = service.AddTask(document, "第二版", string.Empty, clock.Today, RepeatKind.None);

        await repository.SaveAsync(document);

        Assert.IsTrue(File.Exists(paths.LastGoodDataFile));
    }

    [TestMethod]
    public async Task Load_UsesLastGoodWhenPrimaryIsCorrupt()
    {
        using var directory = new TestDirectory();
        var paths = new AppPaths(directory.Path);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var repository = new TaskRepository(paths, clock);
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "可恢复", string.Empty, clock.Today, RepeatKind.None);
        await repository.SaveAsync(document);
        _ = service.AddTask(document, "产生上一版", string.Empty, clock.Today, RepeatKind.None);
        await repository.SaveAsync(document);
        await File.WriteAllTextAsync(paths.DataFile, "{not-json");

        var recovered = await repository.LoadAsync();

        Assert.AreEqual(1, recovered.Tasks.Count);
        Assert.AreEqual("可恢复", recovered.Tasks[0].Title);
    }

    [TestMethod]
    public async Task Import_InvalidDocumentDoesNotReplaceCurrentData()
    {
        using var directory = new TestDirectory();
        var paths = new AppPaths(directory.Path);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.FromHours(8)));
        var repository = new TaskRepository(paths, clock);
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        _ = service.AddTask(document, "保留我", string.Empty, clock.Today, RepeatKind.None);
        await repository.SaveAsync(document);
        var invalidFile = System.IO.Path.Combine(directory.Path, "invalid.json");
        await File.WriteAllTextAsync(invalidFile, "{\"schemaVersion\":999,\"tasks\":[],\"recurrences\":[]}");

        _ = await Assert.ThrowsExactlyAsync<DocumentValidationException>(() => repository.ImportAsync(invalidFile));
        var loaded = await repository.LoadAsync();

        Assert.AreEqual("保留我", loaded.Tasks.Single().Title);
    }
}
