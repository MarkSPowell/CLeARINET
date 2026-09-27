using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>Fiddler's <c>oSession.oResponse</c> -- see <see cref="Exchange"/>'s own remarks for the naming/mutability rationale shared by every type in this folder.</summary>
public sealed class ExchangeResponse
{
    private ExchangeResponse(string httpVersion, int statusCode, string reasonPhrase, ExchangeHeaders headerCollection, byte[] body)
    {
        HttpVersion = httpVersion;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        headers = headerCollection;
        Body = body;
    }

    public static ExchangeResponse From(CapturedResponse response) =>
        new(response.HttpVersion, response.StatusCode, response.ReasonPhrase, new ExchangeHeaders(response.Headers), response.Body);

    /// <summary>Before any response exists yet -- see <see cref="Exchange.ForRequest"/>'s remarks on why <c>OnBeforeRequest</c> needs something non-null here even though real Fiddler wouldn't expect it to be read at that point either.</summary>
    public static ExchangeResponse Empty => new(string.Empty, 0, string.Empty, new ExchangeHeaders([]), []);

    public string HttpVersion { get; set; }

    public int StatusCode { get; set; }

    public string ReasonPhrase { get; set; }

    /// <summary>Fiddler's <c>oResponse.headers</c> -- lowercase to match script usage; see <see cref="ExchangeHeaders"/>.</summary>
    public ExchangeHeaders headers { get; }

    public byte[] Body { get; set; }

    public CapturedResponse ToCapturedResponse() => new(HttpVersion, StatusCode, ReasonPhrase, headers.ToList(), Body);
}
