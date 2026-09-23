using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>Fiddler's <c>oSession.oRequest</c> -- see <see cref="Exchange"/>'s own remarks for the naming/mutability rationale shared by every type in this folder.</summary>
public sealed class ExchangeRequest
{
    private ExchangeRequest(string method, string pathAndQuery, string httpVersion, ExchangeHeaders headerCollection, byte[] body)
    {
        Method = method;
        PathAndQuery = pathAndQuery;
        HttpVersion = httpVersion;
        headers = headerCollection;
        Body = body;
    }

    public static ExchangeRequest From(CapturedRequest request) =>
        new(request.Method, request.Target, request.HttpVersion, new ExchangeHeaders(request.Headers), request.Body);

    /// <summary>Fiddler's <c>oRequest.method</c> in some docs, but every cookbook sample reached through <c>oSession.HTTPMethodIs()</c> instead -- kept here mainly so <see cref="Exchange.HTTPMethodIs"/> has something to read.</summary>
    public string Method { get; set; }

    /// <summary>The request line's target (path + query) -- backs <see cref="Exchange.PathAndQuery"/> and <see cref="Exchange.url"/>.</summary>
    public string PathAndQuery { get; set; }

    public string HttpVersion { get; set; }

    /// <summary>Fiddler's <c>oRequest.headers</c> -- lowercase to match script usage; see <see cref="ExchangeHeaders"/>.</summary>
    public ExchangeHeaders headers { get; }

    public byte[] Body { get; set; }

    public CapturedRequest ToCapturedRequest() => new(Method, PathAndQuery, HttpVersion, headers.ToList(), Body);
}
