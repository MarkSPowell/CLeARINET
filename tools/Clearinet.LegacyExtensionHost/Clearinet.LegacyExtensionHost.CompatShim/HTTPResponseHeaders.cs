namespace Clearinet.CompatShim;

/// <summary>
/// metadata: <c>Fiddler.HTTPResponseHeaders.string HTTPResponseStatus</c>
/// (field, not a property) -- referenced by ContentBlock and Differ.
///
/// Defaults to <see cref="string.Empty"/>, not <c>null</c> -- see
/// <see cref="HTTPRequestHeaders"/>'s own remarks for why (found via a real
/// <see cref="System.NullReferenceException"/> in <c>Differ.dll</c>).
/// </summary>
public sealed class HTTPResponseHeaders : HTTPHeaders
{
    public string HTTPResponseStatus = string.Empty;
}
