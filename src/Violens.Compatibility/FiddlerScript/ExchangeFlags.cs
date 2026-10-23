namespace Violens.Compatibility.FiddlerScript;

/// <summary>Fiddler's <c>oSession.oFlags</c>: just the members scripts use, over the exchange's flags.</summary>
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
