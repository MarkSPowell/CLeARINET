namespace Violens.ProxyCore.Preferences;

/// <summary>One changed preference, shaped like Fiddler's <c>PrefChangeEventArgs</c> plus the old value.</summary>
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
