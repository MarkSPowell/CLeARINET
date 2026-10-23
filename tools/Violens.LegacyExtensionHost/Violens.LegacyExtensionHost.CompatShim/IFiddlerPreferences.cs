using System.Collections.Generic;

namespace Violens.CompatShim;

/// <summary>
/// Members confirmed by metadata (all from ContentBlock):
/// <c>GetStringPref(string,string)</c>, <c>GetBoolPref(string,bool)</c>,
/// <c>SetStringPref(string,string)</c>, <c>SetBoolPref(string,bool)</c>.
/// </summary>
public interface IFiddlerPreferences
{
    string GetStringPref(string prefName, string defaultValue);
    bool GetBoolPref(string prefName, bool defaultValue);
    void SetStringPref(string prefName, string value);
    void SetBoolPref(string prefName, bool value);
}

/// <summary>In-memory storage behind <see cref="FiddlerApplication.Prefs"/>.</summary>
public sealed class InMemoryFiddlerPreferences : IFiddlerPreferences
{
    private readonly Dictionary<string, string> _stringPrefs = new();
    private readonly Dictionary<string, bool> _boolPrefs = new();

    public string GetStringPref(string prefName, string defaultValue) =>
        _stringPrefs.TryGetValue(prefName, out var value) ? value : defaultValue;

    public bool GetBoolPref(string prefName, bool defaultValue) =>
        _boolPrefs.TryGetValue(prefName, out var value) ? value : defaultValue;

    public void SetStringPref(string prefName, string value) => _stringPrefs[prefName] = value;

    public void SetBoolPref(string prefName, bool value) => _boolPrefs[prefName] = value;
}
