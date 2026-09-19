using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TodayChecklist.Models;
using TodayChecklist.Services;
using TodayChecklist.ViewModels;

namespace TodayChecklist;

public partial class MainWindow : Window
{
    private const string TaskDragFormat = "TodayChecklist.TaskId";
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly IDataLocationService _locationService;
    private readonly DataMigrationService _migrationService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ITaskRepository _taskRepository;
    private readonly IStartupService _startupService;
    private readonly IDesktopHostService _desktopHost;
    private readonly IAppLogger _logger;
    private readonly AppRuntimeOptions _runtimeOptions;
    private readonly DispatcherTimer _dateTimer;
    private DateOnly _lastObservedDate;
    private Point? _dragStart;
    private string? _draggedTaskId;
    private ScheduleRowViewModel? _dropTarget;
    private bool _savingDisplayOptions;
    private bool _dropAfter;
    private bool _dragInProgress;
    private bool _isMigrating;
    private bool _exitRequested;
    private bool _allowClose;

    public MainWindow(
        MainViewModel viewModel,
        AppSettings settings,
        AppPaths paths,
        IDataLocationService locationService,
        DataMigrationService migrationService,
        ISettingsRepository settingsRepository,
        ITaskRepository taskRepository,
        IStartupService startupService,
        IDesktopHostService desktopHost,
        IAppLogger logger,
        AppRuntimeOptions runtimeOptions)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _startupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        _desktopHost = desktopHost ?? throw new ArgumentNullException(nameof(desktopHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));

        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.ApplyDisplayOptions(_settings.ShowRolloverDays, _settings.ShowDayDividers);
        RecoveryBanner.Visibility = _viewModel.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        if (_runtimeOptions.IsTestMode)
        {
            Title = "今日清单（隔离测试模式）";
            TestModeBanner.Visibility = Visibility.Visible;
            TestModePathText.Text = "所有任务、设置和日志仅写入：" + _runtimeOptions.TestSessionRoot;
        }

        _lastObservedDate = _viewModel.Today;
        _dateTimer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, DateTimer_Tick, Dispatcher);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    public NewTaskDraftViewModel Draft { get; } = new();

    internal bool TestModeBannerIsVisible =>
        TestModeBanner.Visibility == Visibility.Visible
        && !string.IsNullOrWhiteSpace(TestModePathText.Text);

    public async Task ShowFromTrayAsync()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        if (_settings.DisplayMode == DisplayMode.Standard)
        {
            _ = Activate();
        }

        await Task.CompletedTask;
    }

    public async Task ShowSettingsAsync()
    {
        if (_isMigrating)
        {
            return;
        }

        var startupEnabled = _startupService.IsEnabledForCurrentExecutable();
        var dialog = new SettingsWindow(
            _settings,
            startupEnabled,
            _paths.RootDirectory,
            _locationService.DefaultDataRoot,
            _runtimeOptions.IsTestMode)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.RequestedDataParent is not null
            && !await ResolveDraftBeforeDestructiveActionAsync("更改数据位置").ConfigureAwait(true))
        {
            return;
        }

        try
        {
            _startupService.SetEnabled(dialog.StartupEnabled);
            _settings.StartupEnabled = dialog.StartupEnabled;
            _settings.CloseToTray = dialog.CloseToTray;
            _settings.Opacity = dialog.SelectedOpacity;
            _settings.Theme = dialog.SelectedTheme;
            _settings.DisplayMode = dialog.SelectedDisplayMode;
            ApplyAppearance();
            await ApplyDisplayModeAsync().ConfigureAwait(true);
            await SetDisplayOptionsAsync(dialog.ShowRolloverDays, dialog.ShowDayDividers).ConfigureAwait(true);

            if (dialog.RequestedDataParent is not null)
            {
                await MigrateDataAsync(dialog.RequestedDataParent).ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or TargetInvocationException
            or DocumentValidationException
            or StorageLocationException)
        {
            _isMigrating = false;
            _dateTimer.Start();
            _logger.LogError("SETTINGS_APPLY_FAILED", exception);
            MessageBox.Show(this, "设置没有完全应用。原有任务数据未受影响。\n\n" + exception.Message, "今日清单", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入今日清单备份",
            Filter = "今日清单 JSON 备份 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (MessageBox.Show(this, "导入会替换当前清单；替换前会自动备份。是否继续？", "导入备份", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunUiOperationAsync(async () =>
        {
            var imported = await _taskRepository.ImportAsync(dialog.FileName).ConfigureAwait(true);
            await _viewModel.ReplaceDocumentAsync(imported).ConfigureAwait(true);
            if (_viewModel.RefreshForDate())
            {
                await _taskRepository.SaveAsync(_viewModel.GetDocumentSnapshot()).ConfigureAwait(true);
            }
        }, "导入失败；当前数据没有被未验证的文件替换。").ConfigureAwait(true);
    }

    public async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出今日清单备份",
            Filter = "今日清单 JSON 备份 (*.json)|*.json",
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"今日清单备份-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunUiOperationAsync(
            () => _taskRepository.ExportAsync(_viewModel.GetDocumentSnapshot(), dialog.FileName),
            "导出失败；原始数据没有受到影响。").ConfigureAwait(true);
    }

    public async Task ExitAsync()
    {
        if (_exitRequested || _isMigrating)
        {
            return;
        }

        _exitRequested = true;
        if (!await ResolveDraftBeforeDestructiveActionAsync("退出程序").ConfigureAwait(true))
        {
            _exitRequested = false;
            return;
        }

        _allowClose = true;
        _dateTimer.Stop();
        CaptureWindowSettings();
        try
        {
            await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("SETTINGS_SAVE_ON_EXIT_FAILED", exception);
        }

        System.Windows.Application.Current.Shutdown();
    }

    private async Task MigrateDataAsync(string selectedParent)
    {
        if (_runtimeOptions.IsTestMode)
        {
            throw new InvalidOperationException("隔离测试模式禁止迁移数据。");
        }

        _isMigrating = true;
        _dateTimer.Stop();
        await _taskRepository.SaveAsync(_viewModel.GetDocumentSnapshot()).ConfigureAwait(true);
        await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);

        var result = await _migrationService.MigrateAsync(_paths, selectedParent).ConfigureAwait(true);
        if (_logger is IRelocatableAppLogger relocatableLogger)
        {
            relocatableLogger.Relocate(result.NewPaths);
        }

        var message = $"数据已安全迁移到：\n{result.NewPaths.RootDirectory}\n\n程序将退出，请手动重新打开。";
        if (result.ResidualOldPath is not null)
        {
            message += $"\n\n旧位置未能完全清理，请确认新数据正常后手动检查：\n{result.ResidualOldPath}";
        }

        MessageBox.Show(this, message, "数据迁移完成", MessageBoxButton.OK, MessageBoxImage.Information);
        _allowClose = true;
        System.Windows.Application.Current.Shutdown();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreWindowPlacement();
        ApplyAppearance();
        await ApplyDisplayModeAsync().ConfigureAwait(true);
        _dateTimer.Start();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_settings.CloseToTray)
        {
            CaptureWindowSettings();
            Hide();
            _ = SaveSettingsAsync();
            return;
        }

        _ = ExitAsync();
    }

    private async void DateTimer_Tick(object? sender, EventArgs e)
    {
        if (_isMigrating || _lastObservedDate == _viewModel.Today)
        {
            return;
        }

        _lastObservedDate = _viewModel.Today;
        try
        {
            if (_viewModel.RefreshForDate())
            {
                await _taskRepository.SaveAsync(_viewModel.GetDocumentSnapshot()).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError("MIDNIGHT_REFRESH_FAILED", exception);
            try
            {
                var diskDocument = await _taskRepository.LoadAsync().ConfigureAwait(true);
                await _viewModel.ReplaceDocumentAsync(diskDocument).ConfigureAwait(true);
            }
            catch (Exception recoveryException)
            {
                _logger.LogError("MIDNIGHT_RECOVERY_FAILED", recoveryException);
            }
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left
            && e.ButtonState == MouseButtonState.Pressed
            && FindAncestor<Button>(e.OriginalSource as DependencyObject) is null)
        {
            DragMove();
        }
    }

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        Draft.Begin();
        Dispatcher.BeginInvoke(() =>
        {
            _ = DraftTitleBox.Focus();
            DraftTitleBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private async void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        _ = await SaveDraftAsync().ConfigureAwait(true);
    }

    private void CancelDraftButton_Click(object sender, RoutedEventArgs e)
    {
        ResetDraft();
    }

    private async void DraftTitleBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ResetDraft();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            _ = await SaveDraftAsync().ConfigureAwait(true);
        }
    }

    private async void DraftNotesBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ResetDraft();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = await SaveDraftAsync().ConfigureAwait(true);
        }
    }

    private void DraftRepeatBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Draft.RepeatKind = (RepeatKind)Math.Clamp(DraftRepeatBox.SelectedIndex, 0, 4);
    }

    private async Task<bool> SaveDraftAsync()
    {
        var succeeded = await RunUiOperationAsync(
            () => _viewModel.AddAsync(Draft.Title, Draft.Notes, Draft.RepeatKind),
            "无法添加任务。").ConfigureAwait(true);
        if (succeeded)
        {
            ResetDraft();
        }

        return succeeded;
    }

    private void ResetDraft()
    {
        Draft.Reset();
        DraftRepeatBox.SelectedIndex = 0;
    }

    private async Task<bool> ResolveDraftBeforeDestructiveActionAsync(string actionName)
    {
        if (!Draft.IsActive)
        {
            return true;
        }

        if (!Draft.NeedsResolution)
        {
            ResetDraft();
            return true;
        }

        var choice = MessageBox.Show(
            this,
            $"当前有尚未保存的新任务。是否先保存再{actionName}？\n\n选择“否”会放弃草稿。",
            "未保存的新任务",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (choice == MessageBoxResult.No)
        {
            ResetDraft();
            return true;
        }

        return await SaveDraftAsync().ConfigureAwait(true);
    }

    private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TaskItemViewModel item } checkBox)
        {
            await RunUiOperationAsync(
                () => _viewModel.SetCompletedAsync(item.Id, checkBox.IsChecked == true),
                "无法更新任务状态。").ConfigureAwait(true);
        }
    }

    private async void TaskCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2
            || _dragInProgress
            || FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null
            || !TryGetTaskItem(sender, out var item))
        {
            return;
        }

        e.Handled = true;
        await EditTaskAsync(item).ConfigureAwait(true);
    }

    private async Task EditTaskAsync(TaskItemViewModel item)
    {
        var dialog = new EditTaskWindow(item) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await RunUiOperationAsync(
                () => _viewModel.EditAsync(item.Id, dialog.TaskTitle, dialog.TaskNotes, dialog.ApplyToSeries),
                "无法保存任务修改。").ConfigureAwait(true);
        }
    }

    private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaskItem(sender, out var item))
        {
            await DeleteTaskAsync(item).ConfigureAwait(true);
        }
    }

    private async Task DeleteTaskAsync(TaskItemViewModel item)
    {
        var stopSeries = false;
        if (item.IsRecurring)
        {
            var result = MessageBox.Show(
                this,
                "选择“是”会删除此任务并停止以后重复；选择“否”只删除今天这一项。",
                "删除重复任务",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
            {
                return;
            }

            stopSeries = result == MessageBoxResult.Yes;
        }

        await RunUiOperationAsync(
            () => _viewModel.DeleteAsync(item.Id, stopSeries),
            "无法删除任务。").ConfigureAwait(true);
    }

    private void TaskList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _draggedTaskId = null;
        if (_viewModel.IsReadOnly || FindAncestor<CheckBox>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is ScheduleRowViewModel { CanDrag: true } item)
        {
            _dragStart = e.GetPosition(TaskList);
            _draggedTaskId = item.RowKey;
        }
    }

    private void TaskList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not Point start
            || _draggedTaskId is not string taskId
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(TaskList);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _dragInProgress = true;
        var data = new DataObject();
        data.SetData(TaskDragFormat, taskId);
        try
        {
            _ = DragDrop.DoDragDrop(TaskList, data, DragDropEffects.Move);
        }
        finally
        {
            _dragInProgress = false;
            _draggedTaskId = null;
            ClearDropIndicators();
        }
    }

    private void TaskList_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetDraggedTaskId(e.Data, out var taskId))
        {
            e.Effects = DragDropEffects.None;
            ClearDropIndicators();
            return;
        }

        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        var target = container?.DataContext as ScheduleRowViewModel;
        if (target is null || !target.CanDrag)
        {
            target = _viewModel.Rows.LastOrDefault(static item => item.CanDrag);
            _dropAfter = true;
        }
        else
        {
            var pointer = e.GetPosition(container);
            _dropAfter = pointer.Y >= container!.ActualHeight / 2;
        }

        var pointerInList = e.GetPosition(TaskList);
        var scroll = FindVisualChild<ScrollViewer>(TaskList);
        if (pointerInList.Y < 28)
        {
            scroll?.LineUp();
        }
        else if (pointerInList.Y > TaskList.ActualHeight - 28)
        {
            scroll?.LineDown();
        }

        if (target is null || !_viewModel.CanMoveRow(taskId, target.RowKey, _dropAfter))
        {
            e.Effects = DragDropEffects.None;
            ClearDropIndicators();
            return;
        }

        var after = _dropAfter;
        ClearDropIndicators();
        _dropAfter = after;
        _dropTarget = target;
        _dropTarget.SetDropIndicator(_dropAfter);
        e.Effects = DragDropEffects.Move;
    }

    private async void TaskList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!TryGetDraggedTaskId(e.Data, out var taskId) || _dropTarget is null)
        {
            ClearDropIndicators();
            return;
        }

        var target = _dropTarget;
        var after = _dropAfter;
        ClearDropIndicators();
        await RunUiOperationAsync(
            () => _viewModel.MoveRowAsync(taskId, target.RowKey, after),
            "无法保存任务顺序。").ConfigureAwait(true);
    }

    private bool TryGetDraggedTaskId(IDataObject data, out string taskId)
    {
        taskId = string.Empty;
        if (_dragInProgress && data.GetDataPresent(TaskDragFormat)
            && data.GetData(TaskDragFormat) is string value
            && value == _draggedTaskId)
        {
            taskId = value;
            return true;
        }

        return false;
    }

    private void ClearDropIndicators()
    {
        foreach (var item in _viewModel.Rows)
        {
            item.ClearDropIndicator();
        }

        _dropTarget = null;
        _dropAfter = false;
    }

    private async void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        await RunUiOperationAsync(_viewModel.UndoDeleteAsync, "无法撤销删除。").ConfigureAwait(true);
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowSettingsAsync().ConfigureAwait(true);
    }

    private async void DayDividerButton_Click(object sender, RoutedEventArgs e)
    {
        await ToggleDayDividersAsync().ConfigureAwait(true);
    }

    private async void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            if (!e.IsRepeat)
            {
                await ToggleDayDividersAsync().ConfigureAwait(true);
            }
        }
    }

    private async Task ToggleDayDividersAsync()
    {
        if (_savingDisplayOptions || _dragInProgress || _isMigrating)
        {
            return;
        }

        _savingDisplayOptions = true;
        try
        {
            await RunUiOperationAsync(
                () => SetDisplayOptionsAsync(_settings.ShowRolloverDays, !_settings.ShowDayDividers),
                "无法保存显示选项。").ConfigureAwait(true);
        }
        finally
        {
            _savingDisplayOptions = false;
        }
    }

    private async Task SetDisplayOptionsAsync(bool showRolloverDays, bool showDayDividers)
    {
        CaptureWindowSettings();
        var json = JsonSerializer.Serialize(_settings);
        var candidate = JsonSerializer.Deserialize<AppSettings>(json)!;
        candidate.ShowRolloverDays = showRolloverDays;
        candidate.ShowDayDividers = showDayDividers;
        await _settingsRepository.SaveAsync(candidate).ConfigureAwait(true);
        _settings.ShowRolloverDays = showRolloverDays;
        _settings.ShowDayDividers = showDayDividers;
        ClearDropIndicators();
        _viewModel.ApplyDisplayOptions(showRolloverDays, showDayDividers);
    }

    private static T? FindVisualChild<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new HistoryWindow(
            new HistoryViewModel(_viewModel.GetDocumentSnapshot(), _viewModel.Today),
            _settings.Theme)
        {
            Owner = this,
        };
        _ = dialog.ShowDialog();
    }

    private async void HideButton_Click(object sender, RoutedEventArgs e)
    {
        CaptureWindowSettings();
        Hide();
        await SaveSettingsAsync().ConfigureAwait(true);
    }

    private static bool TryGetTaskItem(object sender, out TaskItemViewModel item)
    {
        item = null!;
        if (sender is FrameworkElement { DataContext: TaskItemViewModel candidate })
        {
            item = candidate;
            return true;
        }

        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private async Task<bool> RunUiOperationAsync(Func<Task> operation, string failureMessage)
    {
        try
        {
            await operation().ConfigureAwait(true);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException
            or DocumentValidationException
            or JsonException)
        {
            _logger.LogError("UI_OPERATION_FAILED", exception);
            MessageBox.Show(this, failureMessage + "\n\n" + exception.Message, "今日清单", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private async Task ApplyDisplayModeAsync()
    {
        if (_runtimeOptions.IsTestMode)
        {
            _settings.DisplayMode = DisplayMode.Standard;
        }

        var applied = await _desktopHost.ApplyAsync(this, _settings.DisplayMode).ConfigureAwait(true);
        if (!applied && _settings.DisplayMode == DisplayMode.Desktop)
        {
            _settings.DisplayMode = DisplayMode.Standard;
            await _settingsRepository.SaveAsync(_settings).ConfigureAwait(true);
            MessageBox.Show(this, "桌面模式当前不可用，已安全退回标准模式。", "今日清单", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ApplyAppearance()
    {
        Opacity = _settings.Opacity;
        var dark = _settings.Theme == Models.ThemeMode.Dark;
        Resources["PageBrush"] = BrushFrom(dark ? "#FF20252D" : "#FFF7F9FC");
        Resources["TaskBrush"] = BrushFrom(dark ? "#FF2A3039" : "#FFFFFFFF");
        Resources["SoftBrush"] = BrushFrom(dark ? "#FF37414E" : "#FFE9EEF5");
        Resources["LineBrush"] = BrushFrom(dark ? "#FF414B59" : "#FFD9E1EC");
        Resources["PrimaryTextBrush"] = BrushFrom(dark ? "#FFF1F4F8" : "#FF253247");
        Resources["SecondaryTextBrush"] = BrushFrom(dark ? "#FFB3BFCE" : "#FF69788F");
        Resources["PeriodAccentBrush"] = BrushFrom(dark ? "#FF82B8F2" : "#FF3978C9");
        Resources["PeriodActiveBackgroundBrush"] = BrushFrom(dark ? "#FF263C58" : "#FFE8F1FF");
        Resources["PeriodInactiveBrush"] = BrushFrom(dark ? "#FF9AA6B5" : "#FF8793A3");
        Resources["PeriodInactiveBackgroundBrush"] = BrushFrom(dark ? "#FF303741" : "#FFEEF1F5");
    }

    private static SolidColorBrush BrushFrom(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }

    private void RestoreWindowPlacement()
    {
        Width = Math.Max(MinWidth, _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
        var workArea = SystemParameters.WorkArea;
        Left = Math.Clamp(_settings.WindowLeft ?? workArea.Right - Width - 24, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(_settings.WindowTop ?? workArea.Top + 24, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    private void CaptureWindowSettings()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.WindowWidth = ActualWidth;
        _settings.WindowHeight = ActualHeight;
    }

    private Task SaveSettingsAsync()
    {
        CaptureWindowSettings();
        return _settingsRepository.SaveAsync(_settings);
    }

    internal void PrepareForAutomatedShutdown()
    {
        _allowClose = true;
        _dateTimer.Stop();
    }
}
