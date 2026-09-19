using System.Text.Json;
using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class DayScheduleTests
{
    private static readonly string[] OriginalOrder = ["A", "B", "C"];
    private static readonly string[] MovedOrder = ["B", "C", "A"];

    [TestMethod]
    public void LegacyData_DefaultsToOriginalDisplayAndMorning()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        var task = JsonSerializer.Deserialize<TaskRecord>("{}")!;
        Assert.IsTrue(settings.ShowRolloverDays);
        Assert.IsFalse(settings.ShowDayDividers);
        Assert.AreEqual(DayPeriod.Morning, task.Period);
    }

    [TestMethod]
    public void RolloverToggle_HidesOnlyTheLabel()
    {
        var fixture = Create();
        fixture.Document.Tasks[0].RolloverDays = 4;
        fixture.ViewModel.ApplyDisplayOptions(false, false);
        Assert.IsFalse(fixture.ViewModel.Tasks[0].HasRolledOver);
        Assert.AreEqual(4, fixture.ViewModel.GetDocumentSnapshot().Tasks[0].RolloverDays);
        fixture.ViewModel.ApplyDisplayOptions(true, false);
        Assert.IsTrue(fixture.ViewModel.Tasks[0].HasRolledOver);
        Assert.AreEqual("已顺延 4 天", fixture.ViewModel.Tasks[0].RolloverText);
        Assert.AreEqual(0, fixture.Repository.SaveCount);
    }

    [TestMethod]
    public void EmptySchedule_ShowsThreeDividersWithoutCountingThemAsTasks()
    {
        var fixture = Create(0);
        fixture.ViewModel.ApplyDisplayOptions(true, true);
        Assert.AreEqual(3, fixture.ViewModel.Rows.Count);
        Assert.AreEqual(0, fixture.ViewModel.Tasks.Count);
        Assert.IsFalse(fixture.ViewModel.ShowEmptyHint);
        Assert.AreEqual("今天还没有任务", fixture.ViewModel.SummaryText);
        fixture.ViewModel.ApplyDisplayOptions(true, false);
        Assert.IsEmpty(fixture.ViewModel.Rows);
        Assert.IsTrue(fixture.ViewModel.ShowEmptyHint);
    }

    [TestMethod]
    public async Task MovingDividers_AssignsPeriodsAndKeepsTaskOrderAcrossToggles()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(true, true);
        await vm.MoveRowAsync("divider:Afternoon", vm.Tasks[1].RowKey, false);
        await vm.MoveRowAsync("divider:Evening", vm.Tasks[2].RowKey, false);
        CollectionAssert.AreEqual(new[] { DayPeriod.Morning, DayPeriod.Afternoon, DayPeriod.Evening },
            vm.Tasks.Select(item => item.Task.Period).ToArray());
        var keys = vm.Rows.Select(item => item.RowKey).ToArray();
        vm.ApplyDisplayOptions(true, false);
        CollectionAssert.AreEqual(OriginalOrder, vm.Tasks.Select(item => item.Title).ToArray());
        Assert.AreEqual(3, vm.Rows.Count);
        vm.ApplyDisplayOptions(true, true);
        CollectionAssert.AreEqual(keys, vm.Rows.Select(item => item.RowKey).ToArray());
    }

    [TestMethod]
    public async Task MorningCanMoveBelowTasksWithoutCrossingAfternoon()
    {
        var fixture = Create();
        fixture.ViewModel.ApplyDisplayOptions(true, true);
        await fixture.ViewModel.MoveRowAsync("divider:Morning", fixture.ViewModel.Tasks[0].RowKey, true);
        Assert.AreEqual(DayPeriod.Unscheduled, fixture.ViewModel.Tasks[0].Task.Period);
        Assert.AreEqual(fixture.ViewModel.Tasks[0], fixture.ViewModel.Rows[0]);
        Assert.AreEqual("divider:Morning", fixture.ViewModel.Rows[1].RowKey);
    }

    [TestMethod]
    [DataRow("divider:Morning", "divider:Afternoon", true)]
    [DataRow("divider:Morning", "divider:Evening", true)]
    [DataRow("divider:Afternoon", "divider:Morning", false)]
    [DataRow("divider:Afternoon", "divider:Evening", true)]
    [DataRow("divider:Evening", "divider:Morning", false)]
    [DataRow("divider:Evening", "divider:Afternoon", false)]
    public async Task CrossingDividers_IsRejectedWithoutSaving(string source, string target, bool after)
    {
        var fixture = Create();
        fixture.ViewModel.ApplyDisplayOptions(true, true);
        var keys = fixture.ViewModel.Rows.Select(item => item.RowKey).ToArray();
        Assert.IsFalse(fixture.ViewModel.CanMoveRow(source, target, after));
        await fixture.ViewModel.MoveRowAsync(source, target, after);
        CollectionAssert.AreEqual(keys, fixture.ViewModel.Rows.Select(item => item.RowKey).ToArray());
        Assert.AreEqual(0, fixture.Repository.SaveCount);
    }

    [TestMethod]
    public async Task TaskCanMoveIntoEmptyEvening_AndCompleteThenReopenInThatPeriod()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(true, true);
        var task = vm.Tasks[0];
        await vm.MoveRowAsync(task.RowKey, "divider:Evening", true);
        Assert.AreEqual(DayPeriod.Evening, vm.Tasks.Single(item => item.Id == task.Id).Task.Period);
        await vm.SetCompletedAsync(task.Id, true);
        Assert.AreEqual("已完成", ((DayDividerViewModel)vm.Rows[^2]).Title);
        Assert.IsFalse(vm.CanMoveRow(task.RowKey, "divider:Morning", false));
        Assert.AreEqual("已完成 1 / 3", vm.SummaryText);
        await vm.SetCompletedAsync(task.Id, false);
        Assert.AreEqual(task.Id, ((TaskItemViewModel)vm.Rows[^1]).Id);
    }

    [TestMethod]
    public async Task DisabledMode_StillSupportsDropBeforeAndAfter()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        await vm.MoveRowAsync(vm.Tasks[0].RowKey, vm.Tasks[2].RowKey, true);
        CollectionAssert.AreEqual(MovedOrder, vm.Tasks.Select(item => item.Title).ToArray());
        await vm.MoveRowAsync(vm.Tasks[2].RowKey, vm.Tasks[0].RowKey, false);
        CollectionAssert.AreEqual(OriginalOrder, vm.Tasks.Select(item => item.Title).ToArray());
    }

    [TestMethod]
    public async Task FailedSave_RestoresDividerPositionsAndTaskPeriods()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(true, true);
        fixture.Repository.FailSaves = true;
        var keys = vm.Rows.Select(item => item.RowKey).ToArray();
        await Assert.ThrowsAsync<IOException>(() => vm.MoveRowAsync("divider:Afternoon", vm.Tasks[1].RowKey, false));
        CollectionAssert.AreEqual(keys, vm.Rows.Select(item => item.RowKey).ToArray());
        Assert.IsTrue(vm.Tasks.All(item => item.Task.Period == DayPeriod.Morning));
    }

    [TestMethod]
    public async Task ToggleAfterAddingReopeningAndFlatReordering_KeepsVisibleOrder()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(true, true);
        var first = vm.Tasks[0];
        await vm.MoveRowAsync(first.RowKey, "divider:Evening", true);
        await vm.SetCompletedAsync(first.Id, true);
        await vm.AddAsync("D", string.Empty, RepeatKind.None);
        await vm.SetCompletedAsync(first.Id, false);
        var keys = vm.Tasks.Select(item => item.RowKey).ToArray();
        vm.ApplyDisplayOptions(true, false);
        CollectionAssert.AreEqual(keys, vm.Tasks.Select(item => item.RowKey).ToArray());
        await vm.MoveRowAsync(first.RowKey, vm.Tasks[0].RowKey, false);
        var reordered = vm.Tasks.Select(item => item.RowKey).ToArray();
        vm.ApplyDisplayOptions(true, true);
        CollectionAssert.AreEqual(reordered, vm.Tasks.Select(item => item.RowKey).ToArray());
    }

    [TestMethod]
    public async Task ReadOnlyMode_PreventsTaskAndDividerChanges()
    {
        var fixture = Create(readOnly: true);
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(false, true);
        Assert.IsFalse(vm.CanMoveRow("divider:Afternoon", vm.Tasks[0].RowKey, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.MoveRowAsync("divider:Afternoon", vm.Tasks[0].RowKey, false));
        Assert.AreEqual(0, fixture.Repository.SaveCount);
    }

    [TestMethod]
    public async Task DeleteUndoAndRollover_PreserveAssignedPeriod()
    {
        var fixture = Create();
        var vm = fixture.ViewModel;
        vm.ApplyDisplayOptions(true, true);
        var id = vm.Tasks[0].Id;
        await vm.MoveRowAsync(vm.Tasks[0].RowKey, "divider:Evening", true);
        await vm.DeleteAsync(id, false);
        await vm.UndoDeleteAsync();
        var document = vm.GetDocumentSnapshot();
        fixture.Service.PrepareForDate(document, fixture.Clock.Today.AddDays(1));
        Assert.AreEqual(DayPeriod.Evening, document.Tasks.Single(task => task.Id == id).Period);
        Assert.AreEqual(1, document.Tasks.Single(task => task.Id == id).RolloverDays);
    }

    [TestMethod]
    public async Task Repository_RoundTripsLayoutThroughSaveLoadAndExportImport()
    {
        using var directory = new TestDirectory();
        var fixture = Create();
        fixture.ViewModel.ApplyDisplayOptions(true, true);
        await fixture.ViewModel.MoveRowAsync("divider:Afternoon", fixture.ViewModel.Tasks[1].RowKey, false);
        var repository = new TaskRepository(new AppPaths(directory.Path), fixture.Clock);
        await repository.SaveAsync(fixture.ViewModel.GetDocumentSnapshot());
        var loaded = await repository.LoadAsync();
        Assert.AreEqual(DayPeriod.Afternoon, loaded.Tasks[1].Period);
        var export = Path.Combine(directory.Path, "export.json");
        await repository.ExportAsync(loaded, export);
        var imported = await repository.ImportAsync(export);
        var restored = new MainViewModel(imported, repository, fixture.Service, fixture.Clock, new NoOpLogger());
        restored.ApplyDisplayOptions(true, true);
        CollectionAssert.AreEqual(fixture.ViewModel.Rows.Select(row => row.RowKey).ToArray(),
            restored.Rows.Select(row => row.RowKey).ToArray());
    }

    [TestMethod]
    public async Task Settings_RoundTripWithRecoverablePreviousCopy()
    {
        using var directory = new TestDirectory();
        var paths = new AppPaths(directory.Path);
        var repository = new SettingsRepository(paths);
        await repository.SaveAsync(new AppSettings());
        var before = await File.ReadAllTextAsync(paths.SettingsFile);
        await repository.SaveAsync(new AppSettings { ShowRolloverDays = false, ShowDayDividers = true });
        var loaded = await repository.LoadAsync();
        Assert.IsFalse(loaded.ShowRolloverDays);
        Assert.IsTrue(loaded.ShowDayDividers);
        Assert.AreEqual(before, await File.ReadAllTextAsync(paths.SettingsFile + ".bak"));
        await Assert.ThrowsAsync<DocumentValidationException>(() =>
            repository.SaveAsync(new AppSettings { Opacity = 0 }));
        Assert.IsTrue((await repository.LoadAsync()).ShowDayDividers);
    }

    [TestMethod]
    public void InvalidPeriod_IsRejected()
    {
        var fixture = Create();
        fixture.Document.Tasks[0].Period = (DayPeriod)999;
        Assert.ThrowsExactly<DocumentValidationException>(() => DocumentValidator.Validate(fixture.Document));
    }

    private static Fixture Create(int count = 3, bool readOnly = false)
    {
        var clock = new TestClock(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.FromHours(8)));
        var service = new TaskService(clock);
        var document = new TaskDataDocument();
        for (var index = 0; index < count; index++)
        {
            service.AddTask(document, ((char)('A' + index)).ToString(), string.Empty, clock.Today, RepeatKind.None);
        }

        var repository = new MemoryRepository();
        return new Fixture(document, repository, clock, service,
            new MainViewModel(document, repository, service, clock, new NoOpLogger(), readOnly));
    }

    private sealed record Fixture(TaskDataDocument Document, MemoryRepository Repository, TestClock Clock,
        TaskService Service, MainViewModel ViewModel);

    private sealed class MemoryRepository : ITaskRepository
    {
        public int SaveCount { get; private set; }
        public bool FailSaves { get; set; }
        public Task<TaskDataDocument> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskDataDocument());
        public Task SaveAsync(TaskDataDocument document, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            DocumentValidator.Validate(document);
            return FailSaves ? Task.FromException(new IOException("simulated")) : Task.CompletedTask;
        }
        public Task ExportAsync(TaskDataDocument document, string destination, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<TaskDataDocument> ImportAsync(string source, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskDataDocument());
    }

    private sealed class NoOpLogger : IAppLogger
    {
        public void Info(string eventCode) { }
        public void LogError(string eventCode, Exception exception) { }
    }
}
