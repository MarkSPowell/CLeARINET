using Violens.ProxyCore.Http;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// The session object FiddlerScript handlers receive. Named <c>Exchange</c>,
/// as in Clearinet's samples, but its members keep Fiddler Classic's exact
/// names and casing (<c>hostname</c>, <c>responseCode</c>), since Jint
/// resolves script property names against them and existing
/// <c>CustomRules.js</c> files use them. Wraps editable copies of the
/// request and response; <see cref="ToRequest"/> and <see cref="ToResponse"/>
/// return the results.
/// </summary>
public sealed class Exchange
{
    private readonly Dictionary<string, string> _flags;

    public Exchange(int id, string hostname, ExchangeRequest request, ExchangeResponse response, IReadOnlyDictionary<string, string>? flags = null)
    {
        id_ = id;
        this.hostname = hostname;
        oRequest = request;
        oResponse = response;
        _flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (flags is not null)
        {
            foreach (var (name, value) in flags)
            {
                _flags[name] = value;
            }
        }

        oFlags = new ExchangeFlags(_flags);
    }

    /// <summary>
    /// For <c>OnBeforeRequest</c>: there's no response yet, so
    /// <see cref="oResponse"/> is empty.
    /// </summary>
    public static Exchange ForRequest(int id, string hostname, CapturedRequest request) =>
        new(id, hostname, ExchangeRequest.From(request), ExchangeResponse.Empty);

    /// <summary>For <c>OnBeforeResponse</c> and <c>OnPeekAtResponseHeaders</c>.</summary>
    public static Exchange ForResponse(int id, string hostname, CapturedRequest request, CapturedResponse response) =>
        new(id, hostname, ExchangeRequest.From(request), ExchangeResponse.From(response));

    // C# won't let a property and its field differ only by case, hence the
    // underscore.
    private readonly int id_;

    /// <summary>Fiddler's <c>oSession.id</c> -- the session's ordinal, matching <see cref="Violens.ProxyCore.Sessions.Session.Id"/>.</summary>
    public int id => id_;

    /// <summary>
    /// Fiddler's <c>oSession.hostname</c>. Settable, as scripts expect, but
    /// changing it doesn't reroute the request yet.
    /// </summary>
    public string hostname { get; set; }

    /// <summary>Fiddler's <c>oSession.url</c>: <c>hostname</c> plus the request's path and query, reconstructed fresh on every read since either half can change independently.</summary>
    public string url => $"{hostname}{oRequest.PathAndQuery}";

    /// <summary>Fiddler's <c>oSession.PathAndQuery</c> -- get/set passthrough to <see cref="ExchangeRequest.PathAndQuery"/>.</summary>
    public string PathAndQuery
    {
        get => oRequest.PathAndQuery;
        set => oRequest.PathAndQuery = value;
    }

    /// <summary>Fiddler's <c>oSession.bypassGateway</c>. Recorded, but doesn't affect routing yet.</summary>
    public bool bypassGateway { get; set; }

    /// <summary>Fiddler's <c>oSession.responseCode</c> -- read-only passthrough, 0 before a response exists (see <see cref="ForRequest"/>).</summary>
    public int responseCode => oResponse.StatusCode;

    /// <summary>Fiddler's <c>oSession.requestBodyBytes</c> -- get/set passthrough.</summary>
    public byte[] requestBodyBytes
    {
        get => oRequest.Body;
        set => oRequest.Body = value;
    }

    /// <summary>Fiddler's <c>oSession.responseBodyBytes</c> -- get/set passthrough.</summary>
    public byte[] responseBodyBytes
    {
        get => oResponse.Body;
        set => oResponse.Body = value;
    }

    public ExchangeRequest oRequest { get; }

    public ExchangeResponse oResponse { get; }

    /// <summary>
    /// Fiddler's <c>oSession.oFlags</c>, for scripts that call
    /// <c>oFlags.Remove(...)</c>. Shares its flags with the indexer.
    /// </summary>
    public ExchangeFlags oFlags { get; }

    /// <summary>
    /// Fiddler's <c>oSession["flag-name"]</c>: UI hints (<c>ui-color</c>,
    /// <c>ui-hide</c>), debug flags (<c>x-breakrequest</c>) and the like, which
    /// most scripts rely on. Case-insensitive, as in Fiddler.
    /// </summary>
    public string? this[string flagName]
    {
        get => _flags.GetValueOrDefault(flagName);
        set
        {
            if (value is null)
            {
                _flags.Remove(flagName);
            }
            else
            {
                _flags[flagName] = value;
            }
        }
    }

    /// <summary>Fiddler's <c>oSession.HostnameIs(sHostname)</c> -- ordinal, case-insensitive, matching real DNS hostname comparison semantics.</summary>
    public bool HostnameIs(string candidateHostname) =>
        string.Equals(hostname, candidateHostname, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fiddler's <c>oSession.HTTPMethodIs(sMethod)</c>.</summary>
    public bool HTTPMethodIs(string method) =>
        string.Equals(oRequest.Method, method, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fiddler's <c>oSession.uriContains(sText)</c> -- checked against <see cref="url"/>, matching real Fiddler's own "the whole URL, not just the path" behavior.</summary>
    public bool uriContains(string text) =>
        url.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fiddler's <c>oSession.utilDecodeResponse()</c>: undoes Content-Encoding in place.</summary>
    public void utilDecodeResponse() => ExchangeCodec.DecodeInPlace(oResponse);

    /// <summary>Fiddler's <c>oSession.utilFindInResponse(sText, bCaseSensitive)</c> -- returns the character index, or -1, matching Fiddler's own contract (not a bool).</summary>
    public int utilFindInResponse(string text, bool caseSensitive = false) =>
        IndexOfInBody(oResponse.Body, text, caseSensitive);

    /// <summary>Fiddler's <c>oSession.utilFindInRequest(sText, bCaseSensitive)</c>.</summary>
    public int utilFindInRequest(string text, bool caseSensitive = false) =>
        IndexOfInBody(oRequest.Body, text, caseSensitive);

    /// <summary>Fiddler's <c>oSession.utilReplaceInResponse(sSearch, sReplace)</c> -- returns the number of replacements made, matching Fiddler's own contract.</summary>
    public int utilReplaceInResponse(string search, string replace)
    {
        var text = System.Text.Encoding.UTF8.GetString(oResponse.Body);
        // Counted against the ORIGINAL text before replacing, not inferred
        // from a before/after length difference -- a same-length
        // replacement (as short a case as "foo" -> "qux") would otherwise
        // make every occurrence look like zero replacements happened.
        var count = CountOccurrences(text, search);
        oResponse.Body = System.Text.Encoding.UTF8.GetBytes(text.Replace(search, replace, StringComparison.OrdinalIgnoreCase));
        return count;
    }

    /// <summary>Fiddler's <c>oSession.utilSetResponseBody(sBody)</c> -- replaces the whole body with a plain UTF-8 string, same as real Fiddler's own convenience helper.</summary>
    public void utilSetResponseBody(string body) => oResponse.Body = System.Text.Encoding.UTF8.GetBytes(body);

    /// <summary>Fiddler's <c>oSession.SaveResponseBody(sFilename)</c> -- writes the raw response bytes as-is (not decoded), matching real Fiddler's own behavior.</summary>
    public void SaveResponseBody(string filename) => File.WriteAllBytes(filename, oResponse.Body);

    private static int IndexOfInBody(byte[] body, string text, bool caseSensitive)
    {
        var haystack = System.Text.Encoding.UTF8.GetString(body);
        return haystack.IndexOf(text, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>The request as the script left it.</summary>
    public CapturedRequest ToRequest() => oRequest.ToCapturedRequest();

    /// <summary>The response as the script left it.</summary>
    public CapturedResponse ToResponse() => oResponse.ToCapturedResponse();
}
