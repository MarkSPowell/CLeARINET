namespace Violens.ProxyCore.Preferences;

/// <summary>Returned by <see cref="IPreferenceStore.AddWatcher"/>, for <see cref="IPreferenceStore.RemoveWatcher"/>.</summary>
public sealed class PrefWatcher
{
    internal PrefWatcher(string prefixFilter, EventHandler<PrefChangeEventArgs> handler)
    {
        PrefixFilter = prefixFilter;
        Handler = handler;
    }

    public string PrefixFilter { get; }

    internal EventHandler<PrefChangeEventArgs> Handler { get; }

    internal bool Matches(string prefName) =>
        prefName.StartsWith(PrefixFilter, StringComparison.OrdinalIgnoreCase);
}
