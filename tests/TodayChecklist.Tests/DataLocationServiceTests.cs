using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class DataLocationServiceTests
{
    [TestMethod]
    public async Task Resolve_FirstRunCreatesPointerAndOwnedDefaultStore()
    {
        using var directory = new TestDirectory();
        var localAppData = Path.Combine(directory.Path, "local");
        var service = new DataLocationService(localAppData);

        var paths = await service.ResolveAsync();

        Assert.AreEqual(Path.Combine(localAppData, "TodayChecklist"), paths.RootDirectory);
        Assert.AreNotEqual(Guid.Empty, paths.StoreId);
        Assert.IsTrue(File.Exists(paths.MarkerFile));
        Assert.IsTrue(File.Exists(Path.Combine(service.BootstrapDirectory, "location.json")));
    }

    [TestMethod]
    public async Task Resolve_UsesLastGoodWhenPrimaryPointerIsCorrupt()
    {
        using var directory = new TestDirectory();
        var service = new DataLocationService(Path.Combine(directory.Path, "local"));
        var paths = await service.ResolveAsync();
        await service.ActivateAsync(paths.RootDirectory, paths.StoreId);
        await File.WriteAllTextAsync(Path.Combine(service.BootstrapDirectory, "location.json"), "{broken");

        var recovered = await service.ResolveAsync();

        Assert.AreEqual(paths.RootDirectory, recovered.RootDirectory);
        Assert.AreEqual(paths.StoreId, recovered.StoreId);
    }

    [TestMethod]
    public async Task Resolve_RejectsWhenBothPointerFilesAreCorrupt()
    {
        using var directory = new TestDirectory();
        var service = new DataLocationService(Path.Combine(directory.Path, "local"));
        var paths = await service.ResolveAsync();
        await service.ActivateAsync(paths.RootDirectory, paths.StoreId);
        await File.WriteAllTextAsync(Path.Combine(service.BootstrapDirectory, "location.json"), "{broken");
        await File.WriteAllTextAsync(Path.Combine(service.BootstrapDirectory, "location.last-good.json"), "{also-broken");

        var exception = await Assert.ThrowsExactlyAsync<StorageLocationException>(() => service.ResolveAsync());

        Assert.AreEqual(StorageLocationFailure.InvalidLocator, exception.Failure);
    }

    [TestMethod]
    public async Task Resolve_DoesNotFallbackWhenCustomDrivePathIsMissing()
    {
        using var directory = new TestDirectory();
        var service = new DataLocationService(Path.Combine(directory.Path, "local"));
        _ = await service.ResolveAsync();
        var missing = Path.Combine(directory.Path, "removed-drive", "TodayChecklist");
        await service.ActivateAsync(missing, Guid.NewGuid());

        var exception = await Assert.ThrowsExactlyAsync<StorageLocationException>(() => service.ResolveAsync());

        Assert.AreEqual(StorageLocationFailure.Unavailable, exception.Failure);
        Assert.IsFalse(Directory.Exists(missing));
    }
}

