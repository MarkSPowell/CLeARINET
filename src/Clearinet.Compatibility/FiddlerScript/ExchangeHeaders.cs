namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Fiddler's <c>oSession.oRequest.headers</c>/<c>oSession.oResponse.headers</c>
/// collection -- an ordered, case-insensitive-by-name header list backing
/// both the indexer form (<c>oSession.oRequest["User-Agent"]</c>) and the
/// method form (<c>.Exists()</c>/<c>.ExistsAndContains()</c>/<c>.Remove()</c>)
/// real scripts use interchangeably (see the cookbook samples referenced
/// from <see cref="Exchange"/>'s own remarks). Backed by a plain
/// <see cref="List{T}"/> of name/value pairs rather than a dictionary,
/// since HTTP headers can legitimately repeat (e.g. multiple <c>Set-Cookie</c>
/// headers) and Fiddler's own header collection preserves that -- a
/// dictionary would silently drop duplicates.
/// </summary>
public sealed class ExchangeHeaders
{
    private readonly List<(string Name, string Value)> _headers;

    public ExchangeHeaders(IEnumerable<(string Name, string Value)> headers) =>
        _headers = new List<(string Name, string Value)>(headers);

    /// <summary>Fiddler's <c>headers["Name"]</c> -- get returns the first matching value or <see langword="null"/>; set replaces the first match or appends if none exists, matching real Fiddler's own "there's only ever one you care about" indexer semantics even though duplicates are preserved underneath.</summary>
    public string? this[string name]
    {
        get
        {
            var index = IndexOf(name);
            return index < 0 ? null : _headers[index].Value;
        }
        set
        {
            if (value is null)
            {
                Remove(name);
                return;
            }

            var index = IndexOf(name);
            if (index < 0)
            {
                _headers.Add((name, value));
            }
            else
            {
                _headers[index] = (name, value);
            }
        }
    }

    /// <summary>Fiddler's <c>headers.Exists("Name")</c>.</summary>
    public bool Exists(string name) => IndexOf(name) >= 0;

    /// <summary>Fiddler's <c>headers.ExistsAndContains("Name", "text")</c> -- case-insensitive substring match against the first matching header's value.</summary>
    public bool ExistsAndContains(string name, string text)
    {
        var value = this[name];
        return value is not null && value.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fiddler's <c>headers.Remove("Name")</c> -- removes every header with this name, not just the first, matching real Fiddler's own behavior for this call specifically (unlike the indexer's "first one wins" read/write).</summary>
    public void Remove(string name) => _headers.RemoveAll(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    private int IndexOf(string name) =>
        _headers.FindIndex(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Snapshots the current header list for handing back to <c>Clearinet.ProxyCore</c> -- see <see cref="Exchange.ToRequest"/>/<see cref="Exchange.ToResponse"/>.</summary>
    public IReadOnlyList<(string Name, string Value)> ToList() => _headers.AsReadOnly();
}
