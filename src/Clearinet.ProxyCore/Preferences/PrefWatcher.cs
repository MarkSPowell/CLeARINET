namespace Clearinet.ProxyCore.Preferences;

/// <summary>
/// The token <see cref="IPreferenceStore.AddWatcher"/> returns, to hand
/// back to <see cref="IPreferenceStore.RemoveWatcher"/>. Opaque on purpose
/// -- the same role Fiddler's own <c>PreferenceBag.PrefWatcher</c> plays.
/// </summary>
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
