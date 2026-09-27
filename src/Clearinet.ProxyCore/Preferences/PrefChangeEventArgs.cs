namespace Clearinet.ProxyCore.Preferences;

/// <summary>
/// One preference that actually changed. Shaped after Fiddler's own
/// documented <c>PrefChangeEventArgs</c> (<see cref="PrefName"/>,
/// <see cref="ValueString"/>, <see cref="ValueBool"/>), plus
/// <see cref="OldValueString"/>, which is cheap to provide here and saves a
/// watcher from keeping its own copy of the previous value.
/// </summary>
public sealed class PrefChangeEventArgs : EventArgs
{
    public PrefChangeEventArgs(string prefName, string? oldValueString, string? valueString)
    {
        PrefName = prefName;
        OldValueString = oldValueString;
        ValueString = valueString;
    }

    public string PrefName { get; }

    /// <summary><see langword="null"/> if the preference was unset before this change.</summary>
    public string? OldValueString { get; }

    /// <summary><see langword="null"/> if this change removed the preference.</summary>
    public string? ValueString { get; }

    /// <summary><see cref="ValueString"/> read as a bool, <see langword="false"/> if it isn't one (or was removed).</summary>
    public bool ValueBool => bool.TryParse(ValueString, out var value) && value;
}
