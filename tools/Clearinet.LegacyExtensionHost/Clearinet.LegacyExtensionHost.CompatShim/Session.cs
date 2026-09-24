using System.Collections.Specialized;
using System.Text;

namespace Clearinet.CompatShim;

/// <summary>
/// The compat shim's own standalone session model -- NOT the same type as
/// <c>Clearinet.ProxyCore</c>'s real session type, and can't be, since this
/// assembly targets net48 while the main app is net10.0. The session
/// bridge (see the design doc's "The legacy host's session bridge --
/// built" section) is what populates instances of this class from real
/// proxied traffic for a loaded extension's <c>IAutoTamper</c> hooks --
/// see <c>SessionMapping</c> (sibling <c>Clearinet.LegacyExtensionHost</c>
/// project) for the wire-to-<c>Session</c> mapping itself. Every member
/// below is confirmed by this project's metadata read of the five real
/// extensions; which sample(s) reference which member is noted per-member.
/// </summary>
public sealed class Session
{
    // metadata: Fiddler.Session.int get_id() -- JSFormat.
    public int id { get; set; }

    // metadata: Fiddler.Session.string get_host() -- ContentBlock, SAZClipboard.
    public string host { get; set; } = string.Empty;

    // metadata: Fiddler.Session.string get_url() -- Differ.
    public string url { get; set; } = string.Empty;

    // metadata: Fiddler.Session.string get_fullUrl() -- ContentBlock, JSFormat.
    public string fullUrl { get; set; } = string.Empty;

    // metadata: Fiddler.Session.string get_PathAndQuery() -- ContentBlock.
    public string PathAndQuery { get; set; } = string.Empty;

    // metadata: Fiddler.Session.int get_responseCode() / set_responseCode(int) -- AustralianImages, JSFormat, Differ.
    public int responseCode { get; set; }

    // metadata: Fiddler.Session.void set_state(Fiddler.SessionStates) -- ContentBlock. See SessionStates.cs's own remarks.
    public SessionStates state { get; set; } = SessionStates.Automatic;

    // metadata: fields, not properties -- Fiddler.Session.Fiddler.ServerChatter oResponse / Fiddler.Session.Fiddler.ClientChatter oRequest.
    // Referenced across all five samples.
    public ServerChatter oResponse = new ServerChatter();
    public ClientChatter oRequest = new ClientChatter();

    // metadata: fields -- Fiddler.Session.byte[] responseBodyBytes / requestBodyBytes.
    // Referenced by AustralianImages, ContentBlock, JSFormat, Differ (response) and SAZClipboard (request).
    public byte[] responseBodyBytes = System.Array.Empty<byte>();
    public byte[] requestBodyBytes = System.Array.Empty<byte>();

    // metadata: Fiddler.Session.System.Collections.Specialized.StringDictionary oFlags -- JSFormat, SAZClipboard.
    public StringDictionary oFlags = new StringDictionary();

    // metadata: Fiddler.Session.void set_Item(string, string) -- ContentBlock ("session flag" indexer,
    // Fiddler Classic's own oSession["ui-color"]-style convention). Backed by oFlags for a sensible
    // implementation, since that's the same shape real Fiddler's own session flags used.
    public string this[string flagName]
    {
        get => oFlags[flagName];
        set => oFlags[flagName] = value;
    }

    /// <summary>metadata: <c>Fiddler.Session.bool uriContains(string)</c> -- ContentBlock.</summary>
    public bool uriContains(string fragment) => fullUrl != null && fullUrl.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>metadata: <c>Fiddler.Session.bool HTTPMethodIs(string)</c> -- ContentBlock, Differ.</summary>
    public bool HTTPMethodIs(string method) => oRequest?.headers?.HTTPMethod != null &&
        string.Equals(oRequest.headers.HTTPMethod, method, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>metadata: <c>Fiddler.Session.bool utilDecodeResponse()</c> -- AustralianImages, JSFormat.</summary>
    public bool utilDecodeResponse()
    {
        // Still a placeholder, now that the session bridge is built and
        // real proxied response bodies do flow through into
        // responseBodyBytes: this method itself doesn't actually decode
        // transfer/content encodings (chunked, gzip, etc.) -- it always
        // reports success without touching the bytes. A loaded legacy
        // extension calling this on a real encoded response gets no real
        // decoding, not an error. Not flagged elsewhere as a known gap
        // yet -- worth adding to the CompatShim README's own list.
        return true;
    }

    /// <summary>metadata: <c>Fiddler.Session.void utilSetResponseBody(string)</c> -- ContentBlock, JSFormat.</summary>
    public void utilSetResponseBody(string body)
    {
        responseBodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
    }

    /// <summary>metadata: <c>Fiddler.Session.void utilCreateResponseAndBypassServer()</c> -- ContentBlock.</summary>
    public void utilCreateResponseAndBypassServer()
    {
        oResponse ??= new ServerChatter();
        state = SessionStates.Done;
    }
}
