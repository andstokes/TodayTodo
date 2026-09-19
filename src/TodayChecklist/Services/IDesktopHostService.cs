using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TodayChecklist.Models;

namespace TodayChecklist.Services;

public interface IDesktopHostService : IDisposable
{
    bool IsAttached { get; }

    Task<bool> ApplyAsync(Window window, DisplayMode mode, CancellationToken cancellationToken = default);
}

public sealed class DesktopHostService : IDesktopHostService
{
    private const uint SpawnWorkerMessage = 0x052C;
    private const uint SendMessageTimeoutNormal = 0x0000;
    private readonly IAppLogger _logger;
    private Window? _window;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private int _taskbarCreatedMessage;
    private bool _disposed;

    public DesktopHostService(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAttached { get; private set; }

    public async Task<bool> ApplyAsync(
        Window window,
        DisplayMode mode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(window);
        EnsureWindowHook(window);

        if (mode == DisplayMode.Standard)
        {
            Detach();
            return true;
        }

        foreach (var delay in new[] { 0, 500, 2_000, 5_000 })
        {
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            }

            var worker = FindDesktopWorker();
            if (worker == IntPtr.Zero)
            {
                continue;
            }

            _ = SetParent(_windowHandle, worker);
            if (GetParent(_windowHandle) == worker)
            {
                IsAttached = true;
                _logger.Info("DESKTOP_MODE_ATTACHED");
                return true;
            }
        }

        Detach();
        _logger.Info("DESKTOP_MODE_FALLBACK");
        return false;
    }

    private void EnsureWindowHook(Window window)
    {
        if (_window == window && _windowHandle != IntPtr.Zero)
        {
            return;
        }

        _window = window;
        _windowHandle = new WindowInteropHelper(window).Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("主窗口尚未完成初始化。");
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WindowMessageHook);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreatedMessage && IsAttached && _window is not null)
        {
            IsAttached = false;
            _ = ApplyAsync(_window, DisplayMode.Desktop);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindDesktopWorker()
    {
        var programManager = FindWindow("Progman", null);
        if (programManager != IntPtr.Zero)
        {
            _ = SendMessageTimeout(
                programManager,
                SpawnWorkerMessage,
                IntPtr.Zero,
                IntPtr.Zero,
                SendMessageTimeoutNormal,
                1_000,
                out _);
        }

        IntPtr worker = IntPtr.Zero;
        _ = EnumWindows((topLevelWindow, _) =>
        {
            var shellView = FindWindowEx(topLevelWindow, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            worker = FindWindowEx(IntPtr.Zero, topLevelWindow, "WorkerW", null);
            return worker == IntPtr.Zero;
        }, IntPtr.Zero);

        return worker;
    }

    private void Detach()
    {
        if (_windowHandle != IntPtr.Zero && GetParent(_windowHandle) != IntPtr.Zero)
        {
            _ = SetParent(_windowHandle, IntPtr.Zero);
        }

        IsAttached = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Detach();
        _source?.RemoveHook(WindowMessageHook);
        _disposed = true;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
#pragma warning restore SYSLIB1054

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr lParam);
}
