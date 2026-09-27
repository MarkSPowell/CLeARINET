namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata: <c>headers</c> property (ContentBlock,
/// SAZClipboard -- <c>Fiddler.ClientChatter.Fiddler.HTTPRequestHeaders get_headers()</c>),
/// indexer getter (ContentBlock -- <c>Fiddler.ClientChatter.string get_Item(string)</c>,
/// likely a request-header shortcut), <c>FailSession(int, string, string)</c>
/// (ContentBlock).
/// </summary>
public sealed class ClientChatter
{
    public HTTPRequestHeaders headers { get; set; } = new HTTPRequestHeaders();

    /// <summary>metadata: getter only confirmed (<c>get_Item</c>) -- likely delegates to <see cref="headers"/>.</summary>
    public string this[string headerName] => headers[headerName];

    /// <summary>
    /// metadata: <c>Fiddler.ClientChatter.void FailSession(int, string, string)</c>
    /// -- ContentBlock. Real Fiddler's own parameter meaning (status code,
    /// short reason, body?) isn't confirmed from metadata alone; named
    /// generically here since only the signature, not the semantics, is
    /// what binary compatibility requires.
    /// </summary>
    public void FailSession(int statusCode, string statusText, string body)
    {
        // Still a placeholder, even with the session bridge built: the
        // wire protocol (BridgeMessageKind/WireRequest/WireResponse in the
        // sibling Bridge project) has no verb for "abort/fail this
        // session" at all today -- only the four IAutoTamper hooks'
        // ordinary edit-and-continue shape. A loaded legacy extension
        // calling this has no way to actually affect the real request/
        // response passing through CLeARINET's proxy; it's a silent
        // no-op, not a thrown error. Not flagged elsewhere as a known gap
        // yet -- worth adding to the CompatShim README's own list.
    }
}
