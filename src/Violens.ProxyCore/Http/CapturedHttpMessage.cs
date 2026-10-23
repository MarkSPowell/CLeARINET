namespace Violens.ProxyCore.Http;

/// <summary>A fully-read HTTP/1.1 request, with any chunked encoding removed from the body.</summary>
public sealed record CapturedRequest(
    string Method,
    string Target,
    string HttpVersion,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[] Body);

/// <summary>A fully-read HTTP/1.1 response, with any chunked encoding removed from the body.</summary>
public sealed record CapturedResponse(
    string HttpVersion,
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[] Body);
