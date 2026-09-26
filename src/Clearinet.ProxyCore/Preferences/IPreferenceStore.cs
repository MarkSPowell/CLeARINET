namespace Clearinet.ProxyCore.Preferences;

/// <summary>
/// CLeARINET's preferences contract -- see the Preferences Design doc.
///
/// Every member except <see cref="GetListPref"/>/<see cref="SetListPref"/>
/// deliberately matches a member of Fiddler's own documented
/// <c>IFiddlerPreferences</c> by name and shape (tenet 1), so a later
/// adapter for FiddlerCore-style extensions is a thin wrapper rather than
/// a redesign. The two list members are CLeARINET's own addition, for
/// upstream Issue #2 ("Extensible Lists").
///
/// Every value is a string under the hood; the typed accessors are
/// conversions at the edge, always culture-invariant, and a value that
/// fails to convert returns the caller's default rather than throwing.
/// Names are case-insensitive. Safe to call from any thread -- see the
/// design doc's "Threading and deadlock safety" section.
/// </summary>
public interface IPreferenceStore
{
    /// <summary>The raw stored string, or <see langword="null"/> if unset (or the name isn't valid).</summary>
    string? this[string prefName] { get; }

    string GetStringPref(string prefName, string defaultValue);

    bool GetBoolPref(string prefName, bool defaultValue);

    int GetInt32Pref(string prefName, int defaultValue);

    /// <summary>
    /// A semicolon-delimited list, trimmed, with empty entries dropped and
    /// case-insensitive duplicates removed (first occurrence kept).
    /// <paramref name="defaultValue"/> is returned only when the preference
    /// is unset -- an explicitly empty value is an empty list, so a list
    /// preference can be overridden to "nothing".
    /// </summary>
    IReadOnlyList<string> GetListPref(string prefName, IReadOnlyList<string> defaultValue);

    void SetStringPref(string prefName, string value);

    void SetBoolPref(string prefName, bool value);

    void SetInt32Pref(string prefName, int value);

    /// <exception cref="ArgumentException">An entry contains <c>;</c>, which the list format can't represent.</exception>
    void SetListPref(string prefName, IEnumerable<string> values);

    /// <summary>Applies several values as one change -- watchers still see one event per name that actually changed.</summary>
    void SetPrefs(IEnumerable<KeyValuePair<string, string>> prefs);

    void RemovePref(string prefName);

    /// <summary>
    /// Calls <paramref name="handler"/> after any preference whose name
    /// starts with <paramref name="prefixFilter"/> (case-insensitive; empty
    /// means every preference) actually changes. Runs on the thread that
    /// made the change, after the store's own lock is released, so a
    /// handler may freely read or write preferences itself.
    /// </summary>
    PrefWatcher AddWatcher(string prefixFilter, EventHandler<PrefChangeEventArgs> handler);

    void RemoveWatcher(PrefWatcher watcher);
}
