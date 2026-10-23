namespace Violens.DesktopUi.ViewModels;

/// <summary>
/// A checkable Rules menu entry: a <c>RulesOption</c> toggle or one choice of
/// a <c>RulesString</c>. The menu is one flat list, so grouped entries'
/// headers read "Submenu: Option".
/// </summary>
public sealed class RulesMenuEntryViewModel : ViewModelBase
{
    private readonly Action<bool> _onCheckedChanged;
    private bool _isChecked;
    private bool _suppressCallback;

    public RulesMenuEntryViewModel(string header, bool initialChecked, Action<bool> onCheckedChanged)
    {
        Header = header;
        _isChecked = initialChecked;
        _onCheckedChanged = onCheckedChanged;
    }

    public string Header { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            _isChecked = value;
            RaisePropertyChanged();

            if (!_suppressCallback)
            {
                _onCheckedChanged(value);
            }
        }
    }

    /// <summary>Updates the checkmark without running the callback, for clearing radio siblings.</summary>
    public void SetCheckedWithoutNotifying(bool value)
    {
        if (_isChecked == value)
        {
            return;
        }

        _suppressCallback = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _suppressCallback = false;
        }
    }
}
