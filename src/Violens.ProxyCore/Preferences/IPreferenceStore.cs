namespace Violens.ProxyCore.Preferences;

/// <summary>
/// Preferences (see the Preferences Design). Apart from the list methods,
/// members match Fiddler's <c>IFiddlerPreferences</c>. Values are strings;
/// typed getters return the default when a value doesn't convert. Names are
/// case-insensitive. Thread-safe.
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
