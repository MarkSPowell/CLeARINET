namespace Clearinet.ProxyCore.Http;

/// <summary>
/// A fully-read HTTP/1.1 request: the parsed start line and headers, plus
/// the decoded body (chunked transfer-encoding already unwrapped, so this
/// is always the logical content, never the wire framing around it).
/// </summary>
public sealed record CapturedRequest(
    string Method,
    string Target,
    string HttpVersion,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[] Body);

/// <summary>
/// A fully-read HTTP/1.1 response, decoded the same way as
/// <see cref="CapturedRequest"/>.
/// </summary>
public sealed record CapturedResponse(
    string HttpVersion,
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[] Body);
