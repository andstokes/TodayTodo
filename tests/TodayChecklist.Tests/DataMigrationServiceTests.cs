using TodayChecklist.Models;
using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class DataMigrationServiceTests
{
    [TestMethod]
    public async Task Migrate_MovesValidatedStoreAndLeavesOnlyBootstrapPointer()
    {
        using var directory = new TestDirectory();
        var fixture = await CreateFixtureAsync(directory.Path);
        var targetParent = Path.Combine(directory.Path, "destination");
        Directory.CreateDirectory(targetParent);

        var result = await fixture.Migration.MigrateAsync(fixture.Paths, targetParent);

        Assert.AreEqual(Path.Combine(targetParent, "TodayChecklist"), result.NewPaths.RootDirectory);
        Assert.IsNull(result.ResidualOldPath);
        Assert.IsTrue(File.Exists(result.NewPaths.DataFile));
        Assert.IsTrue(File.Exists(result.NewPaths.SettingsFile));
        Assert.IsTrue(File.Exists(result.NewPaths.MarkerFile));
        Assert.IsTrue(Directory.EnumerateDirectories(result.NewPaths.BackupDirectory, "migration-*").Any());
        Assert.IsFalse(Directory.Exists(fixture.Paths.DataDirectory));
        Assert.IsFalse(File.Exists(fixture.Paths.SettingsFile));
        Assert.IsFalse(File.Exists(fixture.Paths.MarkerFile));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Location.BootstrapDirectory, "location.json")));

        var resolved = await fixture.Location.ResolveAsync();
        Assert.AreEqual(result.NewPaths.RootDirectory, resolved.RootDirectory);
        var loaded = await new TaskRepository(resolved, fixture.Clock).LoadAsync();
        Assert.AreEqual("迁移任务", loaded.Tasks.Single().Title);

        await File.WriteAllTextAsync(Path.Combine(fixture.Location.BootstrapDirectory, "location.json"), "{broken");
        var recoveredFromPointerBackup = await fixture.Location.ResolveAsync();
        Assert.AreEqual(result.NewPaths.RootDirectory, recoveredFromPointerBackup.RootDirectory);
    }

    [TestMethod]
    public async Task Migrate_RejectsNonEmptyTargetWithoutChangingPointer()
    {
        using var directory = new TestDirectory();
        var fixture = await CreateFixtureAsync(directory.Path);
        var targetParent = Path.Combine(directory.Path, "occupied");
        var targetRoot = Path.Combine(targetParent, "TodayChecklist");
        Directory.CreateDirectory(targetRoot);
        await File.WriteAllTextAsync(Path.Combine(targetRoot, "user-file.txt"), "keep");

        _ = await Assert.ThrowsExactlyAsync<DocumentValidationException>(() =>
            fixture.Migration.MigrateAsync(fixture.Paths, targetParent));

        var resolved = await fixture.Location.ResolveAsync();
        Assert.AreEqual(fixture.Paths.RootDirectory, resolved.RootDirectory);
        Assert.IsTrue(File.Exists(Path.Combine(targetRoot, "user-file.txt")));
    }

    [TestMethod]
    public async Task Migrate_InvalidCopiedSettingsKeepsSourceActive()
    {
        using var directory = new TestDirectory();
        var fixture = await CreateFixtureAsync(directory.Path);
        await File.WriteAllTextAsync(fixture.Paths.SettingsFile, "{invalid");
        var targetParent = Path.Combine(directory.Path, "invalid-target");
        Directory.CreateDirectory(targetParent);

        _ = await Assert.ThrowsAsync<Exception>(() => fixture.Migration.MigrateAsync(fixture.Paths, targetParent));

        var resolved = await fixture.Location.ResolveAsync();
        Assert.AreEqual(fixture.Paths.RootDirectory, resolved.RootDirectory);
        Assert.IsTrue(File.Exists(fixture.Paths.DataFile));
        Assert.IsFalse(Directory.Exists(Path.Combine(targetParent, "TodayChecklist")));
    }

    [TestMethod]
    public async Task Migrate_CleanupFailureKeepsNewStoreActiveAndReportsResidual()
    {
        using var directory = new TestDirectory();
        var fixture = await CreateFixtureAsync(directory.Path);
        Directory.CreateDirectory(fixture.Paths.LogDirectory);
        var log = Path.Combine(fixture.Paths.LogDirectory, "locked.log");
        await File.WriteAllTextAsync(log, "event");
        var targetParent = Path.Combine(directory.Path, "locked-source-target");
        Directory.CreateDirectory(targetParent);

        DataMigrationResult result;
        using (var lockStream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await fixture.Migration.MigrateAsync(fixture.Paths, targetParent);
        }

        Assert.AreEqual(fixture.Paths.RootDirectory, result.ResidualOldPath);
        var resolved = await fixture.Location.ResolveAsync();
        Assert.AreEqual(result.NewPaths.RootDirectory, resolved.RootDirectory);
        Assert.IsTrue(File.Exists(result.NewPaths.DataFile));
    }

    private static async Task<MigrationFixture> CreateFixtureAsync(string root)
    {
        var location = new DataLocationService(Path.Combine(root, "local"));
        var paths = await location.ResolveAsync();
        var clock = new TestClock(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(8)));
        var taskRepository = new TaskRepository(paths, clock);
        var document = new TaskDataDocument();
        _ = new TaskService(clock).AddTask(document, "迁移任务", "正文", clock.Today, RepeatKind.None);
        await taskRepository.SaveAsync(document);
        await new SettingsRepository(paths).SaveAsync(new AppSettings());
        return new MigrationFixture(location, paths, clock, new DataMigrationService(location, clock));
    }

    private sealed record MigrationFixture(
        DataLocationService Location,
        AppPaths Paths,
        TestClock Clock,
        DataMigrationService Migration);
}
