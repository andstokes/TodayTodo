namespace TodayChecklist.ViewModels;

public abstract class ScheduleRowViewModel : ObservableObject
{
    public abstract string RowKey { get; }

    public abstract bool CanDrag { get; }

    public bool ShowDropBefore { get; private set; }

    public bool ShowDropAfter { get; private set; }

    public void SetDropIndicator(bool after)
    {
        ShowDropBefore = !after;
        ShowDropAfter = after;
        OnPropertyChanged(nameof(ShowDropBefore));
        OnPropertyChanged(nameof(ShowDropAfter));
    }

    public void ClearDropIndicator()
    {
        ShowDropBefore = false;
        ShowDropAfter = false;
        OnPropertyChanged(nameof(ShowDropBefore));
        OnPropertyChanged(nameof(ShowDropAfter));
    }
}
