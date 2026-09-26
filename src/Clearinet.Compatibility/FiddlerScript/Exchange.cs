using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// The FiddlerScript-facing request/response wrapper a <c>Handlers</c>
/// function receives as its single argument -- Fiddler Classic's own
/// <c>Session</c> object, under the name <c>ericlaw1979/Clearinet</c>'s own
/// sample scripts have already moved to (see that repo's
/// <c>Content/SampleRules.js</c>: <c>static function OnBeforeRequest(oEx:
/// Exchange)</c>, with the file's own comment noting "in the SAZ format,
/// the term 'Exchange' is written as 'Session'"). This project follows that
/// precedent for the *type name* deliberately -- it costs nothing, since a
/// FiddlerScript type annotation like <c>oSession: Session</c> is erased
/// entirely by <see cref="FiddlerScriptPreprocessor"/> before Jint ever
/// sees it (script code never constructs one of these or references its
/// .NET type name at all, only whatever parameter name the script itself
/// chose) -- and it means CLeARINET's own compatibility layer isn't
/// presumptively guessing at upstream's naming the way <c>Session.cs</c>'s
/// own doc comment, written before this was actually checked, turned out
/// to be wrong about.
///
/// The *member* surface is a different call, made deliberately the other
/// way: every property and method below keeps Fiddler Classic's own exact
/// casing (<c>hostname</c>, not <c>Hostname</c>; <c>responseCode</c>, not
/// <c>ResponseCode</c>) even where that violates normal C# naming
/// conventions, because Jint's default CLR interop resolves JS property
/// access against the real member name, and an *existing* <c>CustomRules.js</c>
/// file (the actual porting target -- see the Project Plan's "Fiddler
/// Classic compatibility review") was written against those exact names,
/// not ericlaw1979/Clearinet's newer, partially-renamed
/// <c>Exchange</c> sample (which uses <c>host</c>/<c>urlContains()</c>
/// instead of <c>hostname</c>/<c>uriContains()</c> in a few places).
/// Aliasing those newer member names too is future work, not done here --
/// see the FiddlerScript Compatibility Design doc.
///
/// Wraps a *mutable* working copy of the request/response rather than
/// <see cref="Clearinet.ProxyCore.Http.CapturedRequest"/>/<c>CapturedResponse</c>
/// directly, since those are immutable records (built for a session that's
/// already finished, per <c>Session.cs</c>'s own remarks) and a script
/// needs to actually edit headers/body/routing before the request goes
/// anywhere. <see cref="ToRequest"/>/<see cref="ToResponse"/> project this
/// working state back into fresh immutable records once a handler
/// finishes -- not called by anything yet (see
/// <see cref="FiddlerScriptHost"/>'s remarks: this phase stops at running a
/// handler and inspecting its effects, not yet at wiring those effects
/// into <c>InterceptingProxyListener</c>'s actual request/response flow).
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
    /// Builds a working <see cref="Exchange"/> from an already-captured,
    /// immutable request -- the normal entry point once a request has been
    /// fully read off the wire but before it's forwarded, matching where
    /// Fiddler Classic's own <c>OnBeforeRequest</c> fires. There's no
    /// response yet at that point, so <see cref="oResponse"/> starts empty;
    /// a script that reads response members from inside
    /// <c>OnBeforeRequest</c> (nothing in the real API contract expects
    /// that to work either) just sees blank/default values.
    /// </summary>
    public static Exchange ForRequest(int id, string hostname, CapturedRequest request) =>
        new(id, hostname, ExchangeRequest.From(request), ExchangeResponse.Empty);

    /// <summary>
    /// Builds a working <see cref="Exchange"/> once a response exists too --
    /// where Fiddler Classic's own <c>OnBeforeResponse</c>/
    /// <c>OnPeekAtResponseHeaders</c> fire.
    /// </summary>
    public static Exchange ForResponse(int id, string hostname, CapturedRequest request, CapturedResponse response) =>
        new(id, hostname, ExchangeRequest.From(request), ExchangeResponse.From(response));

    // Named id_ rather than "id" only in the backing field; the public
    // member below is the real, script-visible one. C# doesn't allow a
    // property and its own backing field to share a name case-insensitively
    // without an explicit field keyword (not yet used elsewhere in this
    // codebase), so a trailing underscore on the private field is simpler
    // than introducing that dependency here.
    private readonly int id_;

    /// <summary>Fiddler's <c>oSession.id</c> -- the session's ordinal, matching <see cref="Clearinet.ProxyCore.Sessions.Session.Id"/>.</summary>
    public int id => id_;

    /// <summary>
    /// Fiddler's <c>oSession.hostname</c> -- get/set, since real scripts
    /// retarget a request by writing to it (see the "Retarget Server Port"
    /// and "Simulate HOSTS File" cookbook patterns). Settable here for API
    /// fidelity; nothing downstream reads the new value back yet (see this
    /// type's own remarks on <see cref="ToRequest"/> not being wired up).
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

    /// <summary>Fiddler's <c>oSession.bypassGateway</c>: skip the configured upstream gateway/proxy for this one exchange. Recorded, not yet wired to routing -- see this type's own remarks.</summary>
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
    /// Fiddler's <c>oSession.oFlags</c> -- the same generic string bag the
    /// indexer below reads and writes, exposed as its own object because
    /// real scripts call <c>oSession.oFlags.Remove("ui-hide")</c> (see the
    /// "Unhide 404 Responses" cookbook sample) as well as the indexer form.
    /// Both views share <see cref="_flags"/>, so a write through either one
    /// is visible from the other immediately.
    /// </summary>
    public ExchangeFlags oFlags { get; }

    /// <summary>
    /// Fiddler's own <c>oSession["flag-name"]</c> generic property bag --
    /// UI hints (<c>ui-color</c>/<c>ui-bold</c>/<c>ui-hide</c>/
    /// <c>ui-customcolumn</c>), debug flags (<c>x-breakrequest</c>/
    /// <c>x-breakresponse</c>/<c>x-replywithfile</c>/<c>x-overrideHost</c>),
    /// trickle-delay knobs, and timing (<c>X-TTFB</c>/<c>X-TTLB</c>) all go
    /// through here rather than a typed property per flag -- see the
    /// FiddlerScript Compatibility Design doc's note on why this one
    /// indexer is disproportionately high-value: most cookbook samples lean
    /// on it rather than a named member. Case-insensitive, matching real
    /// Fiddler's own flag-name handling.
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

    /// <summary>
    /// Fiddler's <c>oSession.utilDecodeResponse()</c>: undoes
    /// Content-Encoding in place, same as <c>RawTextInspector</c>'s own
    /// decoding (its twin implementation, not shared code -- see
    /// <see cref="ExchangeCodec"/>'s remarks for why this duplicates rather
    /// than cross-references the Extensibility-layer inspector).
    /// </summary>
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

    /// <summary>Projects the current working state back into an immutable <see cref="CapturedRequest"/>. Not called by anything yet -- see this type's own remarks.</summary>
    public CapturedRequest ToRequest() => oRequest.ToCapturedRequest();

    /// <summary>Projects the current working state back into an immutable <see cref="CapturedResponse"/>. Not called by anything yet -- see this type's own remarks.</summary>
    public CapturedResponse ToResponse() => oResponse.ToCapturedResponse();
}
