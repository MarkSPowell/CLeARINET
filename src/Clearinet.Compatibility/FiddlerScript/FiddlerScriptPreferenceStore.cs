using System.Text.Json;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Backs <see cref="BindPrefBinding"/>'s persisted half: a small,
/// string-keyed JSON file under the local app-data folder, the same
/// <c>%LocalAppData%\CLeARINET\...</c> convention
/// <c>Clearinet.ProxyCore.SystemProxy.WinInetSystemProxy</c>'s own crash-safety
/// backup file already established (see that class's own remarks) --
/// reused here rather than inventing a second settings convention.
///
/// A preference whose name contains "Ephemeral" (case-insensitive, matching
/// real Fiddler's own documented <c>fiddlerscript.ephemeral.*</c> naming
/// convention) is kept in this instance's own in-memory dictionary but never
/// written to disk -- <see cref="FiddlerScriptRunner"/> owns exactly one
/// instance of this class for its whole lifetime, so an ephemeral value set
/// during one script load is still there for a later
/// <see cref="FiddlerScriptRunner.Reload"/> within the same run (matching
/// "preserved across recompiles of the script within a single Fiddler
/// instance"), but starts fresh -- <see langword="null"/> -- the next time
/// CLeARINET itself starts (matching "discarded each time Fiddler exits").
///
/// One instance is not safe to share across concurrent
/// <see cref="Get"/>/<see cref="Set"/> calls from different threads without
/// external synchronization -- the same "plain mutable state, caller's own
/// responsibility" posture the rest of this codebase's own rule/script state
/// already takes (see <see cref="FiddlerScriptHost"/>'s own remarks).
/// </summary>
public sealed class FiddlerScriptPreferenceStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, string> _values;

    /// <param name="filePath">
    /// Overrides the default <c>%LocalAppData%\CLeARINET\fiddlerscript-prefs.json</c>
    /// path -- exists purely so tests can point this at a temp file instead
    /// of touching a real machine's app-data folder; a production caller is
    /// expected to leave this at its default.
    /// </param>
    public FiddlerScriptPreferenceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CLeARINET",
            "fiddlerscript-prefs.json");

        _values = LoadFromDisk();
    }

    /// <summary><see langword="null"/> if this preference has never been set (or was set only as an ephemeral value in a previous process run -- see this class's own remarks).</summary>
    public string? Get(string prefName) => _values.GetValueOrDefault(prefName);

    /// <summary>
    /// Records <paramref name="value"/> against <paramref name="prefName"/>,
    /// immediately -- there's no separate "save on unload" step (see
    /// <see cref="BindPrefBinding"/>'s own remarks on why
    /// <see cref="FiddlerScriptRunner"/> writes through on every set rather
    /// than batching until some unload event that doesn't reliably exist in
    /// this codebase yet). Writing through immediately, on every call, is
    /// strictly more crash-safe than real Fiddler's own "save at unload"
    /// timing, not less -- an app crash between a script-driven change and
    /// the next unload can't lose it here.
    /// </summary>
    public void Set(string prefName, string value)
    {
        _values[prefName] = value;

        if (!IsEphemeral(prefName))
        {
            SaveToDisk();
        }
    }

    private static bool IsEphemeral(string prefName) => prefName.Contains("ephemeral", StringComparison.OrdinalIgnoreCase);

    private Dictionary<string, string> LoadFromDisk()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath));
            return loaded ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable prefs file shouldn't take the whole
            // script host down over what's ultimately just cached
            // convenience state -- every BindPref field's own script-side
            // `= default` initializer is still a perfectly working fallback.
            return new Dictionary<string, string>();
        }
    }

    private void SaveToDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_values));
    }
}
