using Forms = System.Windows.Forms;

namespace TodayChecklist.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly System.Drawing.Icon _appIcon;

    public TrayIconService(bool isTestMode = false)
    {
        var menu = new Forms.ContextMenuStrip();
        _ = menu.Items.Add(
            isTestMode ? "显示隔离测试清单" : "显示今日清单",
            null,
            (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        _ = menu.Items.Add("设置", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        if (!isTestMode)
        {
            _ = menu.Items.Add("导入备份…", null, (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty));
            _ = menu.Items.Add("导出备份…", null, (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty));
        }

        _ = menu.Items.Add(new Forms.ToolStripSeparator());
        _ = menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _appIcon = LoadApplicationIcon();
        _icon = new Forms.NotifyIcon
        {
            Text = isTestMode ? "今日清单（隔离测试）" : "今日清单",
            Icon = _appIcon,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ImportRequested;

    public event EventHandler? ExportRequested;

    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _appIcon.Dispose();
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is not null)
            {
                return icon;
            }
        }

        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }
}
