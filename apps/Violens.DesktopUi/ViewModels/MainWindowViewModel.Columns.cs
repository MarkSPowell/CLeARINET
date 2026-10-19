using Violens.AppCore.Preferences;
using Violens.ProxyCore.Preferences;

namespace Violens.DesktopUi.ViewModels;

/// <summary>
/// Which session-list columns are hidden (right-click a column header, or
/// Tools > Session List Columns), and their order and widths. Columns are known by a key: the header
/// for the built-in columns, <c>script:</c> plus the title for a
/// FiddlerScript <c>[BindUIColumn]</c>, <c>flag:</c> plus the flag name for
/// an extension's column. A hidden column stays hidden across runs, and a
/// script or extension column comes back hidden when that script or
/// extension is loaded again.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Columns that can't be hidden: without them the list doesn't say which session is which.</summary>
    private static readonly HashSet<string> s_lockedColumns = new(StringComparer.OrdinalIgnoreCase) { "#", "URL" };

    private HashSet<string>? _hiddenColumns;

    private HashSet<string> HiddenColumns =>
        _hiddenColumns ??= new HashSet<string>(
            _preferences.GetListPref(PreferenceKeys.HiddenSessionColumns, []),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>True for a column that's always shown (<c>#</c> and <c>URL</c>).</summary>
    public static bool IsColumnLocked(string key) => s_lockedColumns.Contains(key);

    /// <summary>Whether the column with <paramref name="key"/> is shown.</summary>
    public bool IsColumnVisible(string key) => IsColumnLocked(key) || !HiddenColumns.Contains(key);

    /// <summary>Shows or hides a column, and remembers it.</summary>
    public void SetColumnVisible(string key, bool visible)
    {
        // ';' separates entries in a list preference.
        key = key.Replace(";", string.Empty, StringComparison.Ordinal).Trim();
        if (key.Length == 0 || IsColumnLocked(key))
        {
            return;
        }

        var changed = visible ? HiddenColumns.Remove(key) : HiddenColumns.Add(key);
        if (changed)
        {
            _preferences.SetListPref(PreferenceKeys.HiddenSessionColumns, HiddenColumns.Order(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Shows every column again, including script and extension columns not loaded right now.</summary>
    public void ShowAllColumns()
    {
        if (HiddenColumns.Count == 0)
        {
            return;
        }

        HiddenColumns.Clear();
        _preferences.SetListPref(PreferenceKeys.HiddenSessionColumns, []);
    }

    /// <summary>The saved column order and widths; empty if the user hasn't changed them.</summary>
    public IReadOnlyList<ColumnLayoutEntry> LoadColumnLayout() =>
        ColumnLayout.Parse(_preferences.GetListPref(PreferenceKeys.SessionColumnLayout, []));

    /// <summary>
    /// Remembers the columns' order and widths. Columns saved before but not
    /// there now (a script or extension that isn't loaded) keep their place.
    /// </summary>
    public void SaveColumnLayout(IReadOnlyList<ColumnLayoutEntry> current)
    {
        var merged = ColumnLayout.Merge(LoadColumnLayout(), current);
        var formatted = ColumnLayout.Format(merged).ToList();
        if (!formatted.SequenceEqual(_preferences.GetListPref(PreferenceKeys.SessionColumnLayout, [])))
        {
            _preferences.SetListPref(PreferenceKeys.SessionColumnLayout, formatted);
        }
    }

    /// <summary>Forgets the column order and widths (Reset Columns).</summary>
    public void ClearColumnLayout() => _preferences.SetListPref(PreferenceKeys.SessionColumnLayout, []);
}
