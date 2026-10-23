namespace Violens.CompatShim;

/// <summary>
/// <c>HTTPResponseStatus</c> (a field), used by ContentBlock and Differ.
/// Defaults to empty rather than null, as <see cref="HTTPRequestHeaders"/>
/// does.
/// </summary>
public sealed class HTTPResponseHeaders : HTTPHeaders
{
    public string HTTPResponseStatus = string.Empty;
}
