using System.Windows;
using TodayChecklist.Models;

namespace TodayChecklist.Services;

public sealed class TestModeStartupService : IStartupService
{
    public bool IsEnabledForCurrentExecutable() => false;

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            throw new InvalidOperationException("隔离测试模式禁止创建开机启动快捷方式。");
        }
    }
}

public sealed class TestModeDesktopHostService : IDesktopHostService
{
    public bool IsAttached => false;

    public Task<bool> ApplyAsync(
        Window window,
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(mode == DisplayMode.Standard);
    }

    public void Dispose()
    {
    }
}
