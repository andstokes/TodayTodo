using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class TestModeServicesTests
{
    [TestMethod]
    public void StartupService_CannotEnableStartup()
    {
        var service = new TestModeStartupService();

        Assert.IsFalse(service.IsEnabledForCurrentExecutable());
        service.SetEnabled(enabled: false);
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => service.SetEnabled(enabled: true));
    }
}
