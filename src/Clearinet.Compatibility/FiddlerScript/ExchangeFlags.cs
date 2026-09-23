namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Fiddler's <c>oSession.oFlags</c> -- the same backing dictionary
/// <see cref="Exchange"/>'s own indexer reads and writes, wrapped so real
/// scripts that call <c>oSession.oFlags.Remove("ui-hide")</c> (rather than
/// <c>oSession["ui-hide"] = null</c>) have something to call. Deliberately
/// thin: this exists for exactly the members real cookbook samples use,
/// not a full dictionary-like contract.
/// </summary>
public sealed class ExchangeFlags(Dictionary<string, string> flags)
{
    public string? this[string name]
    {
        get => flags.GetValueOrDefault(name);
        set
        {
            if (value is null)
            {
                flags.Remove(name);
            }
            else
            {
                flags[name] = value;
            }
        }
    }

    public bool Exists(string name) => flags.ContainsKey(name);

    public void Remove(string name) => flags.Remove(name);
}
