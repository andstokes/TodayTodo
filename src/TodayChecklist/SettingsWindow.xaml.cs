using System.Windows;
using System.Windows.Controls;
using TodayChecklist.Models;

namespace TodayChecklist;

public partial class SettingsWindow : Window
{
    private readonly string _currentDataRoot;
    private readonly string _defaultDataRoot;

    public SettingsWindow(
        AppSettings settings,
        bool startupIsEnabled,
        string currentDataRoot,
        string defaultDataRoot,
        bool isTestMode = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultDataRoot);
        _currentDataRoot = Path.GetFullPath(currentDataRoot);
        _defaultDataRoot = Path.GetFullPath(defaultDataRoot);
        InitializeComponent();
        DisplayModeBox.SelectedIndex = (int)settings.DisplayMode;
        ThemeBox.SelectedIndex = (int)settings.Theme;
        OpacitySlider.Value = settings.Opacity;
        StartupBox.IsChecked = startupIsEnabled;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        ShowRolloverDaysBox.IsChecked = settings.ShowRolloverDays;
        ShowDayDividersBox.IsChecked = settings.ShowDayDividers;
        DataPathBox.Text = _currentDataRoot;
        if (isTestMode)
        {
            Title = "今日清单设置（隔离测试模式）";
            DisplayModeBox.SelectedIndex = (int)DisplayMode.Standard;
            DisplayModeBox.IsEnabled = false;
            StartupBox.IsChecked = false;
            StartupBox.IsEnabled = false;
            SelectDataLocationButton.IsEnabled = false;
            RestoreDefaultLocationButton.IsEnabled = false;
            DesktopWarning.Text = "隔离测试模式已禁止桌面层连接、开机启动和数据位置迁移。";
            DesktopWarning.Visibility = Visibility.Visible;
        }
    }

    public DisplayMode SelectedDisplayMode => (DisplayMode)DisplayModeBox.SelectedIndex;

    public Models.ThemeMode SelectedTheme => (Models.ThemeMode)ThemeBox.SelectedIndex;

    public double SelectedOpacity => OpacitySlider.Value;

    public bool StartupEnabled => StartupBox.IsChecked == true;

    public bool CloseToTray => CloseToTrayBox.IsChecked == true;

    public bool ShowRolloverDays => ShowRolloverDaysBox.IsChecked == true;

    public bool ShowDayDividers => ShowDayDividersBox.IsChecked == true;

    public string? RequestedDataParent { get; private set; }

    internal bool TestModeRestrictionsAreActive =>
        !DisplayModeBox.IsEnabled
        && !StartupBox.IsEnabled
        && !SelectDataLocationButton.IsEnabled
        && !RestoreDefaultLocationButton.IsEnabled;

    private void DisplayModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DesktopWarning is not null)
        {
            DesktopWarning.Visibility = DisplayModeBox.SelectedIndex == (int)DisplayMode.Desktop
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SelectDataLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择今日清单数据的父文件夹",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetRequestedParent(dialog.FolderName);
    }

    private void RestoreDefaultLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_defaultDataRoot)?.FullName
            ?? throw new InvalidOperationException("无法确定默认数据目录的父文件夹。");
        SetRequestedParent(parent);
    }

    private void SetRequestedParent(string parent)
    {
        var fullParent = Path.GetFullPath(parent);
        var target = Path.GetFullPath(Path.Combine(fullParent, "TodayChecklist"));
        DataPathBox.Text = target;
        RequestedDataParent = string.Equals(
            Path.TrimEndingDirectorySeparator(target),
            Path.TrimEndingDirectorySeparator(_currentDataRoot),
            StringComparison.OrdinalIgnoreCase)
            ? null
            : fullParent;
    }
}
