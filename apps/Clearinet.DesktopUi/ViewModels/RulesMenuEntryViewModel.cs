namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// One checkable entry in the dynamic "_Rules" menu -- backs both shapes
/// <c>MainWindowViewModel.RefreshScriptMenus</c> builds from a loaded
/// script's <c>FiddlerScriptRunner.Directives</c>: a plain
/// <c>RulesMenuOption</c> boolean toggle, and one
/// <c>RulesStringChoice</c> inside a <c>RulesMenuStringOption</c> submenu
/// (radio-style -- picking one clears its siblings). Both are "a header,
/// a checked state, something that happens when the person (re)checks it"
/// shaped, so one class covers both rather than two near-identical ones.
///
/// See <c>MainWindow.axaml</c>'s own remarks for why this renders as a
/// single flat <c>MenuItem.ItemsSource</c> list rather than real nested
/// submenus -- <c>Header</c> already carries a <c>"Submenu: Option"</c>
/// prefix for a grouped entry, baked in by whoever constructs this (see
/// <c>MainWindowViewModel.BuildRulesMenuEntries</c>), not computed here.
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

    /// <summary>
    /// Two-way bound from the menu item's own <c>IsChecked</c>. Setting
    /// this from user interaction (the normal binding path) runs
    /// <c>onCheckedChanged</c>; see <see cref="SetCheckedWithoutNotifying"/>
    /// for the one case that deliberately doesn't (clearing a radio-group
    /// sibling).
    /// </summary>
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

    /// <summary>
    /// Updates <see cref="IsChecked"/> (and still raises the property-changed
    /// notification the bound menu item needs to redraw) without running
    /// <c>onCheckedChanged</c> -- what a radio group's "checking one clears
    /// the rest" logic calls on every sibling, so clearing them doesn't
    /// itself trigger another round of "set the field, clear the group"
    /// against a field that's about to be overwritten anyway by the entry
    /// that's actually being checked.
    /// </summary>
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
