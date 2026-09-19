using System.Windows;
using System.Windows.Media;
using TodayChecklist.ViewModels;

namespace TodayChecklist;

public partial class HistoryWindow : Window
{
    public HistoryWindow(HistoryViewModel viewModel, Models.ThemeMode theme)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        ApplyAppearance(theme);
    }

    private void ApplyAppearance(Models.ThemeMode theme)
    {
        var dark = theme == Models.ThemeMode.Dark;
        Resources["PageBrush"] = BrushFrom(dark ? "#FF20252D" : "#FFF7F9FC");
        Resources["TaskBrush"] = BrushFrom(dark ? "#FF2A3039" : "#FFFFFFFF");
        Resources["SoftBrush"] = BrushFrom(dark ? "#FF37414E" : "#FFE9EEF5");
        Resources["LineBrush"] = BrushFrom(dark ? "#FF414B59" : "#FFD9E1EC");
        Resources["PrimaryTextBrush"] = BrushFrom(dark ? "#FFF1F4F8" : "#FF253247");
        Resources["SecondaryTextBrush"] = BrushFrom(dark ? "#FFB3BFCE" : "#FF69788F");
    }

    private static SolidColorBrush BrushFrom(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
