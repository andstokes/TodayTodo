using TodayChecklist.Services;

namespace TodayChecklist.Tests;

[TestClass]
public sealed class AppRuntimeOptionsTests
{
    [TestMethod]
    public void Parse_NoArgumentsUsesProductionMode()
    {
        var options = AppRuntimeOptions.Parse([]);

        Assert.IsFalse(options.IsTestMode);
        Assert.IsFalse(options.IsSmokeTest);
        Assert.AreEqual("Production", options.InstanceKey);
        Assert.IsNull(options.TestSessionRoot);
        Assert.IsNull(options.TestLocalApplicationData);
    }

    [TestMethod]
    public void Parse_TestModeCreatesUniqueSessionUnderSuppliedTemporaryRoot()
    {
        using var directory = new TestDirectory();

        var first = AppRuntimeOptions.Parse(["--test-mode"], directory.Path);
        var second = AppRuntimeOptions.Parse(["--test-mode"], directory.Path);

        Assert.IsTrue(first.IsTestMode);
        Assert.IsFalse(first.IsSmokeTest);
        Assert.IsNotNull(first.TestSessionRoot);
        Assert.IsTrue(first.TestSessionRoot.StartsWith(
            Path.Combine(directory.Path, "TodayChecklist-Test") + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(Path.Combine(first.TestSessionRoot, "LocalAppData"), first.TestLocalApplicationData);
        Assert.AreNotEqual(first.TestSessionRoot, second.TestSessionRoot);
        Assert.AreNotEqual(first.InstanceKey, second.InstanceKey);
    }

    [TestMethod]
    public void Parse_SmokeTestRequiresTestMode()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            AppRuntimeOptions.Parse(["--smoke-test"]));
    }

    [TestMethod]
    public void Parse_RejectsUnknownOrDuplicateArguments()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            AppRuntimeOptions.Parse(["--unknown"]));
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            AppRuntimeOptions.Parse(["--test-mode", "--test-mode"]));
    }
}
