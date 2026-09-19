namespace TodayChecklist.Services;

public interface IStartupService
{
    bool IsEnabledForCurrentExecutable();

    void SetEnabled(bool enabled);
}

public sealed class StartupService : IStartupService
{
    private const string ShortcutName = "TodayChecklist.lnk";
    private readonly string _executablePath;
    private readonly string _shortcutPath;

    public StartupService(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        _shortcutPath = Path.Combine(startup, ShortcutName);
    }

    public bool IsEnabledForCurrentExecutable()
    {
        if (!File.Exists(_shortcutPath))
        {
            return false;
        }

        try
        {
            return string.Equals(ReadTarget(_shortcutPath), _executablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or TargetInvocationException)
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateShortcut();
            return;
        }

        if (File.Exists(_shortcutPath) && IsEnabledForCurrentExecutable())
        {
            File.Delete(_shortcutPath);
        }
    }

    private void CreateShortcut()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式服务。");
        dynamic shortcut = shell.CreateShortcut(_shortcutPath);
        shortcut.TargetPath = _executablePath;
        shortcut.WorkingDirectory = Path.GetDirectoryName(_executablePath);
        shortcut.Description = "今日清单";
        shortcut.Save();

        if (!IsEnabledForCurrentExecutable())
        {
            throw new InvalidOperationException("开机启动快捷方式验证失败。");
        }
    }

    private static string? ReadTarget(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建 Windows 快捷方式服务。");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        return shortcut.TargetPath as string;
    }
}
