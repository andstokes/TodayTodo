using System.Windows;
using TodayChecklist.ViewModels;

namespace TodayChecklist;

public partial class EditTaskWindow : Window
{
    public EditTaskWindow(TaskItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        InitializeComponent();
        TitleBox.Text = item.Title;
        NotesBox.Text = item.Notes;
        ApplyToSeriesBox.Visibility = item.IsRecurring ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            _ = TitleBox.Focus();
            TitleBox.SelectAll();
        };
    }

    public string TaskTitle => TitleBox.Text;

    public string TaskNotes => NotesBox.Text;

    public bool ApplyToSeries => ApplyToSeriesBox.IsChecked == true;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            MessageBox.Show(this, "请输入任务标题。", "今日清单", MessageBoxButton.OK, MessageBoxImage.Information);
            _ = TitleBox.Focus();
            return;
        }

        DialogResult = true;
    }
}

