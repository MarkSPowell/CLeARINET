using System.Text.Json;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// Saved values for <c>[BindPref]</c> fields, in a small JSON file in the
/// app's data folder. Names containing "Ephemeral" (Fiddler's
/// <c>fiddlerscript.ephemeral.*</c> convention) are kept in memory only, so
/// they survive a script reload but not a restart. Not thread-safe.
/// </summary>
public sealed class FiddlerScriptPreferenceStore
{
    private readonly string _filePath;
    private readonly Dictionary<string, string> _values;

    /// <param name="filePath">For tests; defaults to <c>fiddlerscript-prefs.json</c> in the app's data folder.</param>
    public FiddlerScriptPreferenceStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Violens.ProxyCore.ProxyEngine.Identity.ResolveDataFolder(),
            "fiddlerscript-prefs.json");

        _values = LoadFromDisk();
    }

    /// <summary>The saved value, or null if there isn't one.</summary>
    public string? Get(string prefName) => _values.GetValueOrDefault(prefName);

    /// <summary>Saves the value straight away, rather than when the script unloads as Fiddler does.</summary>
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
