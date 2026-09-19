namespace TodayChecklist.Services;

public sealed class AppRuntimeOptions
{
    private const string TestModeArgument = "--test-mode";
    private const string SmokeTestArgument = "--smoke-test";

    private AppRuntimeOptions(
        bool isTestMode,
        bool isSmokeTest,
        string instanceKey,
        string? testSessionRoot)
    {
        IsTestMode = isTestMode;
        IsSmokeTest = isSmokeTest;
        InstanceKey = instanceKey;
        TestSessionRoot = testSessionRoot;
        TestLocalApplicationData = testSessionRoot is null
            ? null
            : Path.Combine(testSessionRoot, "LocalAppData");
    }

    public bool IsTestMode { get; }

    public bool IsSmokeTest { get; }

    public string InstanceKey { get; }

    public string? TestSessionRoot { get; }

    public string? TestLocalApplicationData { get; }

    public static AppRuntimeOptions Parse(IReadOnlyList<string> arguments, string? temporaryRoot = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var isTestMode = false;
        var isSmokeTest = false;
        foreach (var argument in arguments)
        {
            if (string.Equals(argument, TestModeArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (isTestMode)
                {
                    throw new ArgumentException("测试模式参数不能重复。", nameof(arguments));
                }

                isTestMode = true;
            }
            else if (string.Equals(argument, SmokeTestArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (isSmokeTest)
                {
                    throw new ArgumentException("烟雾测试参数不能重复。", nameof(arguments));
                }

                isSmokeTest = true;
            }
            else
            {
                throw new ArgumentException($"不支持的启动参数：{argument}", nameof(arguments));
            }
        }

        if (isSmokeTest && !isTestMode)
        {
            throw new ArgumentException("烟雾测试只能在隔离测试模式中运行。", nameof(arguments));
        }

        if (!isTestMode)
        {
            return new AppRuntimeOptions(
                isTestMode: false,
                isSmokeTest: false,
                instanceKey: "Production",
                testSessionRoot: null);
        }

        var sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var baseDirectory = Path.GetFullPath(temporaryRoot ?? Path.GetTempPath());
        var sessionRoot = Path.GetFullPath(Path.Combine(
            baseDirectory,
            "TodayChecklist-Test",
            $"session-{sessionId}"));
        return new AppRuntimeOptions(
            isTestMode: true,
            isSmokeTest,
            instanceKey: $"Test.{sessionId}",
            testSessionRoot: sessionRoot);
    }
}
