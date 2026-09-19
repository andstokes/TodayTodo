using System.Globalization;
using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class SingleInstanceServiceTests
{
    [TestMethod]
    public void DifferentInstanceKeysCanRunIndependently()
    {
        var suffix = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var production = new SingleInstanceService($"Production-{suffix}");
        using var test = new SingleInstanceService($"Test-{suffix}");

        Assert.IsTrue(production.TryAcquire());
        Assert.IsTrue(test.TryAcquire());
    }

    [TestMethod]
    public void SameInstanceKeyCannotBeAcquiredTwice()
    {
        var key = $"Test-{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)}";
        using var first = new SingleInstanceService(key);
        using var second = new SingleInstanceService(key);

        Assert.IsTrue(first.TryAcquire());
        Assert.IsFalse(second.TryAcquire());
    }
}
