using Violens.ProxyCore.Http;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>Fiddler's <c>oSession.oResponse</c>.</summary>
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

    /// <summary>A placeholder for <c>OnBeforeRequest</c>, before there's a response.</summary>
    public static ExchangeResponse Empty => new(string.Empty, 0, string.Empty, new ExchangeHeaders([]), []);

    public string HttpVersion { get; set; }

    public int StatusCode { get; set; }

    public string ReasonPhrase { get; set; }

    /// <summary>Fiddler's <c>oResponse.headers</c> -- lowercase to match script usage; see <see cref="ExchangeHeaders"/>.</summary>
    public ExchangeHeaders headers { get; }

    public byte[] Body { get; set; }

    public CapturedResponse ToCapturedResponse() => new(HttpVersion, StatusCode, ReasonPhrase, headers.ToList(), Body);
}
