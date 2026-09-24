namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata: <c>headers</c> property (AustralianImages,
/// ContentBlock, JSFormat, Differ -- <c>Fiddler.ServerChatter.Fiddler.HTTPResponseHeaders get_headers()</c>),
/// indexer getter (Differ -- <c>Fiddler.ServerChatter.string get_Item(string)</c>).
/// </summary>
public sealed class ServerChatter
{
    public HTTPResponseHeaders headers { get; set; } = new HTTPResponseHeaders();

    public string this[string headerName] => headers[headerName];
}
