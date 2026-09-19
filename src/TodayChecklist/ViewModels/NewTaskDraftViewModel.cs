using TodayChecklist.Models;

namespace TodayChecklist.ViewModels;

public sealed class NewTaskDraftViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private RepeatKind _repeatKind;
    private bool _isActive;
    private bool _isExpanded;

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
            {
                return;
            }

            _title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(NeedsResolution));
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (_notes == value)
            {
                return;
            }

            _notes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(NeedsResolution));
        }
    }

    public RepeatKind RepeatKind
    {
        get => _repeatKind;
        set
        {
            if (_repeatKind == value)
            {
                return;
            }

            _repeatKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContent));
            OnPropertyChanged(nameof(NeedsResolution));
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsResolution));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool HasContent => !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Notes)
        || RepeatKind != RepeatKind.None;

    public bool NeedsResolution => IsActive && HasContent;

    public void Begin()
    {
        IsActive = true;
    }

    public void Reset()
    {
        Title = string.Empty;
        Notes = string.Empty;
        RepeatKind = RepeatKind.None;
        IsExpanded = false;
        IsActive = false;
    }
}
