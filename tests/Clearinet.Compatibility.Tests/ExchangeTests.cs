using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.Compatibility.Tests;

public class ExchangeTests
{
    private static CapturedRequest SampleRequest(string method = "GET", string target = "/widgets?x=1", IReadOnlyList<(string, string)>? headers = null) =>
        new(method, target, "HTTP/1.1", headers ?? [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse(int statusCode = 200, string reasonPhrase = "OK", byte[]? body = null, IReadOnlyList<(string, string)>? headers = null) =>
        new("HTTP/1.1", statusCode, reasonPhrase, headers ?? [], body ?? []);

    [Fact]
    public void ForRequest_ExposesUrlAsHostnamePlusPathAndQuery()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        Assert.Equal("api.example.com/widgets?x=1", exchange.url);
        Assert.Equal("/widgets?x=1", exchange.PathAndQuery);
    }

    [Fact]
    public void ForRequest_ResponseCodeIsZeroBeforeAResponseExists()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        Assert.Equal(0, exchange.responseCode);
    }

    [Fact]
    public void ForResponse_ResponseCodeReflectsTheCapturedStatus()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse(404, "Not Found"));

        Assert.Equal(404, exchange.responseCode);
    }

    [Fact]
    public void HostnameIs_IsOrdinalCaseInsensitive()
    {
        var exchange = Exchange.ForRequest(1, "API.Example.COM", SampleRequest());

        Assert.True(exchange.HostnameIs("api.example.com"));
        Assert.False(exchange.HostnameIs("other.example.com"));
    }

    [Fact]
    public void HTTPMethodIs_IsCaseInsensitive()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest(method: "post"));

        Assert.True(exchange.HTTPMethodIs("POST"));
        Assert.False(exchange.HTTPMethodIs("GET"));
    }

    [Fact]
    public void UriContains_ChecksTheWholeUrlNotJustThePath()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest(target: "/widgets"));

        Assert.True(exchange.uriContains("api.example.com/widgets"));
    }

    [Fact]
    public void Indexer_RoundTripsAFlagAndNullClearsIt()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        exchange["ui-color"] = "red";
        Assert.Equal("red", exchange["ui-color"]);

        exchange["ui-color"] = null;
        Assert.Null(exchange["ui-color"]);
    }

    [Fact]
    public void OFlags_AndTheIndexerShareTheSameBackingBag()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        exchange["x-breakrequest"] = "1";
        Assert.True(exchange.oFlags.Exists("x-breakrequest"));

        exchange.oFlags.Remove("x-breakrequest");
        Assert.Null(exchange["x-breakrequest"]);
    }

    [Fact]
    public void RequestHeaders_IndexerAddsWhenMissingAndReplacesWhenPresent()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        exchange.oRequest.headers["X-Test"] = "one";
        Assert.Equal("one", exchange.oRequest.headers["X-Test"]);

        exchange.oRequest.headers["X-Test"] = "two";
        Assert.Equal("two", exchange.oRequest.headers["X-Test"]);
        Assert.Single(exchange.ToRequest().Headers, h => h.Name == "X-Test");
    }

    [Fact]
    public void Headers_ExistsAndContainsIsCaseInsensitiveOnNameAndValue()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse(
            headers: [("content-type", "TEXT/HTML; charset=utf-8")]));

        Assert.True(exchange.oResponse.headers.ExistsAndContains("Content-Type", "text/html"));
        Assert.False(exchange.oResponse.headers.ExistsAndContains("Content-Type", "application/json"));
    }

    [Fact]
    public void Headers_RemoveDropsEveryMatchingHeaderNotJustTheFirst()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse(
            headers: [("Set-Cookie", "a=1"), ("Set-Cookie", "b=2"), ("Content-Type", "text/plain")]));

        exchange.oResponse.headers.Remove("Set-Cookie");

        Assert.False(exchange.oResponse.headers.Exists("Set-Cookie"));
        Assert.True(exchange.oResponse.headers.Exists("Content-Type"));
    }

    [Fact]
    public void UtilSetResponseBody_ReplacesTheWholeBodyAsUtf8()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        exchange.utilSetResponseBody("hello world");

        Assert.Equal("hello world", System.Text.Encoding.UTF8.GetString(exchange.responseBodyBytes));
    }

    [Fact]
    public void UtilFindInResponse_ReturnsTheCharacterIndexOrMinusOne()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(),
            SampleResponse(body: System.Text.Encoding.UTF8.GetBytes("hello world")));

        Assert.Equal(6, exchange.utilFindInResponse("world"));
        Assert.Equal(-1, exchange.utilFindInResponse("missing"));
    }

    [Fact]
    public void UtilReplaceInResponse_ReplacesEveryOccurrenceAndReturnsTheCount()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(),
            SampleResponse(body: System.Text.Encoding.UTF8.GetBytes("foo bar foo baz foo")));

        var count = exchange.utilReplaceInResponse("foo", "qux");

        Assert.Equal(3, count);
        Assert.Equal("qux bar qux baz qux", System.Text.Encoding.UTF8.GetString(exchange.responseBodyBytes));
    }

    [Fact]
    public void UtilDecodeResponse_UndoesGzipAndRemovesTheContentEncodingHeader()
    {
        var original = "hello, decoded world"u8.ToArray();
        using var compressedStream = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(original);
        }

        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse(
            body: compressedStream.ToArray(),
            headers: [("Content-Encoding", "gzip")]));

        exchange.utilDecodeResponse();

        Assert.Equal(original, exchange.responseBodyBytes);
        Assert.False(exchange.oResponse.headers.Exists("Content-Encoding"));
    }

    [Fact]
    public void UtilDecodeResponse_ZstdIsAnHonestNotSupportedExceptionRatherThanSilentGarbage()
    {
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse(
            body: [1, 2, 3],
            headers: [("Content-Encoding", "zstd")]));

        Assert.Throws<NotSupportedException>(exchange.utilDecodeResponse);
    }

    [Fact]
    public void ToRequest_ProjectsWorkingStateBackIntoAnImmutableCapturedRequest()
    {
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());
        exchange.oRequest.headers["X-Injected"] = "yes";
        exchange.requestBodyBytes = "payload"u8.ToArray();

        var request = exchange.ToRequest();

        Assert.Equal("yes", request.Headers.Single(h => h.Name == "X-Injected").Value);
        Assert.Equal("payload"u8.ToArray(), request.Body);
    }
}
