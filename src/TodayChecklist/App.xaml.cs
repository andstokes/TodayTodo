using System.Windows;
using System.Windows.Threading;
using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WPF Application owns these services for its full lifetime and disposes them in OnExit.")]
public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;
    private TrayIconService? _tray;
    private IDesktopHostService? _desktopHost;
    private FileAppLogger? _logger;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppRuntimeOptions runtimeOptions;
        try
        {
            runtimeOptions = AppRuntimeOptions.Parse(e.Args);
        }
        catch (ArgumentException argumentException)
        {
            MessageBox.Show(
                "启动参数无效，程序尚未读取或创建任何任务数据。\n\n" + argumentException.Message,
                "今日清单",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(64);
            return;
        }

        _singleInstance = new SingleInstanceService(runtimeOptions.InstanceKey);
        if (!_singleInstance.TryAcquire())
        {
            await _singleInstance.SignalExistingAsync().ConfigureAwait(true);
            Shutdown();
            return;
        }

        var localApplicationData = runtimeOptions.TestLocalApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var locationService = new DataLocationService(localApplicationData);
        AppPaths paths;
        while (true)
        {
            try
            {
                paths = await locationService.ResolveAsync().ConfigureAwait(true);
                break;
            }
            catch (StorageLocationException locationException)
            {
                var choice = MessageBox.Show(
                    locationException.Message
                    + "\n\n程序不会创建空清单或回退到 C 盘。请重新连接目标磁盘或修复路径配置后选择“重试”。"
                    + "\n\n位置：" + locationException.Path,
                    "今日清单：数据位置不可用",
                    MessageBoxButton.RetryCancel,
                    MessageBoxImage.Warning);
                if (choice != MessageBoxResult.Retry)
                {
                    Shutdown(2);
                    return;
                }
            }
        }

        var clock = new SystemClock();
        _logger = new FileAppLogger(paths);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        try
        {
            var taskRepository = new TaskRepository(paths, clock);
            var settingsRepository = new SettingsRepository(paths);
            var taskService = new TaskService(clock);
            var migrationService = new DataMigrationService(locationService, clock);
            var settings = await settingsRepository.LoadAsync().ConfigureAwait(true);
            IStartupService startupService;
            if (runtimeOptions.IsTestMode)
            {
                startupService = new TestModeStartupService();
                settings.DisplayMode = DisplayMode.Standard;
                settings.StartupEnabled = false;
            }
            else
            {
                var executable = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定程序启动路径。");
                startupService = new StartupService(executable);
            }

            settings.StartupEnabled = startupService.IsEnabledForCurrentExecutable();

            TaskDataDocument document;
            var readOnly = false;
            try
            {
                document = await taskRepository.LoadAsync().ConfigureAwait(true);
            }
            catch (DataRecoveryException recoveryException)
            {
                _logger.LogError("DATA_RECOVERY_FAILED", recoveryException);
                document = new TaskDataDocument();
                readOnly = true;
                MessageBox.Show(
                    "任务数据及最近的安全副本都无法验证。程序将以只读恢复模式打开，不会覆盖原文件。\n\n数据位置：" + recoveryException.DataFile,
                    "今日清单：数据需要检查",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (!readOnly
                && runtimeOptions.IsTestMode
                && TestDataSeeder.SeedIfEmpty(document, taskService, clock.Today))
            {
                await taskRepository.SaveAsync(document).ConfigureAwait(true);
            }

            if (!readOnly && taskService.PrepareForDate(document, clock.Today))
            {
                await taskRepository.SaveAsync(document).ConfigureAwait(true);
            }

            var viewModel = new MainViewModel(document, taskRepository, taskService, clock, _logger, readOnly);
            _desktopHost = runtimeOptions.IsTestMode
                ? new TestModeDesktopHostService()
                : new DesktopHostService(_logger);
            _mainWindow = new MainWindow(
                viewModel,
                settings,
                paths,
                locationService,
                migrationService,
                settingsRepository,
                taskRepository,
                startupService,
                _desktopHost,
                _logger,
                runtimeOptions);
            MainWindow = _mainWindow;
            ConfigureTray(_mainWindow, runtimeOptions.IsTestMode);

            _singleInstance.ActivationRequested += (_, _) =>
                Dispatcher.BeginInvoke(() => _ = _mainWindow.ShowFromTrayAsync());
            _singleInstance.StartListening();
            _mainWindow.Show();
            _logger.Info("APP_STARTED");
            if (runtimeOptions.IsSmokeTest)
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(() => _ = RunSmokeTestAsync(
                        _mainWindow,
                        viewModel,
                        settings,
                        paths)),
                    DispatcherPriority.ApplicationIdle);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("APP_START_FAILED", exception);
            MessageBox.Show(
                "今日清单无法安全启动，未修改任务数据。\n\n" + exception.Message,
                "今日清单",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ConfigureTray(MainWindow window, bool isTestMode)
    {
        _tray = new TrayIconService(isTestMode);
        _tray.ShowRequested += (_, _) => Dispatcher.BeginInvoke(() => _ = window.ShowFromTrayAsync());
        _tray.SettingsRequested += (_, _) => Dispatcher.BeginInvoke(() => _ = window.ShowSettingsAsync());
        _tray.ImportRequested += (_, _) => Dispatcher.BeginInvoke(() => _ = window.ImportAsync());
        _tray.ExportRequested += (_, _) => Dispatcher.BeginInvoke(() => _ = window.ExportAsync());
        _tray.ExitRequested += (_, _) => Dispatcher.BeginInvoke(() => _ = window.ExitAsync());
    }

    private async Task RunSmokeTestAsync(
        MainWindow mainWindow,
        MainViewModel viewModel,
        AppSettings settings,
        AppPaths paths)
    {
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            if (!mainWindow.IsLoaded
                || mainWindow.ActualWidth <= 0
                || mainWindow.ActualHeight <= 0
                || !mainWindow.TestModeBannerIsVisible)
            {
                throw new InvalidOperationException("主窗口未完成布局或隔离提示未显示。");
            }

            if (!File.Exists(paths.DataFile))
            {
                throw new InvalidOperationException("隔离任务数据没有保存到测试目录。");
            }

            var historyViewModel = new HistoryViewModel(viewModel.GetDocumentSnapshot(), viewModel.Today);
            if (historyViewModel.DayCount < 2)
            {
                throw new InvalidOperationException("历史记录测试数据未正确分组。");
            }

            var historyWindow = new HistoryWindow(historyViewModel, settings.Theme)
            {
                Owner = mainWindow,
            };
            historyWindow.Show();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            if (!historyWindow.IsLoaded || historyWindow.ActualWidth <= 0 || historyWindow.ActualHeight <= 0)
            {
                throw new InvalidOperationException("历史记录窗口未完成布局。");
            }

            historyWindow.Close();
            viewModel.ApplyDisplayOptions(false, true);
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            if (viewModel.Rows.OfType<DayDividerViewModel>().Count(row => row.CanDrag) != 3
                || viewModel.Tasks.Any(static item => item.HasRolledOver))
            {
                throw new InvalidOperationException("时段或顺延显示选项没有生效。");
            }

            viewModel.ApplyDisplayOptions(settings.ShowRolloverDays, settings.ShowDayDividers);
            var settingsWindow = new SettingsWindow(
                settings,
                startupIsEnabled: false,
                paths.RootDirectory,
                paths.RootDirectory,
                isTestMode: true)
            {
                Owner = mainWindow,
            };
            settingsWindow.Show();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
            if (!settingsWindow.IsLoaded
                || settingsWindow.ActualWidth <= 0
                || settingsWindow.ActualHeight <= 0
                || !settingsWindow.TestModeRestrictionsAreActive)
            {
                throw new InvalidOperationException("测试模式设置窗口未正确加载或安全限制未生效。");
            }

            settingsWindow.Close();
            _logger?.Info("SMOKE_TEST_PASSED");
            mainWindow.PrepareForAutomatedShutdown();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _logger?.LogError("SMOKE_TEST_FAILED", exception);
            mainWindow.PrepareForAutomatedShutdown();
            Shutdown(1);
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError("UNHANDLED_UI_EXCEPTION", e.Exception);
        MessageBox.Show(
            "程序遇到未处理错误，将安全退出。已保存的数据不会被主动删除。\n\n" + e.Exception.Message,
            "今日清单",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("APP_EXITED");
        _tray?.Dispose();
        _desktopHost?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
