using System.Text;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class HttpMessageTextTests
{
    [Fact]
    public void Format_ThenTryParseRequest_RoundTripsAnUneditedRequest()
    {
        var original = new CapturedRequest(
            "POST", "/foo?x=1", "HTTP/1.1", [("Host", "example.test")], "hello"u8.ToArray());

        var text = HttpMessageText.Format(original);
        var ok = HttpMessageText.TryParseRequest(text, out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal("POST", request!.Method);
        Assert.Equal("/foo?x=1", request.Target);
        Assert.Contains(request.Headers, h => h.Name == "Host" && h.Value == "example.test");
        Assert.Equal("hello", Encoding.UTF8.GetString(request.Body));
    }

    [Fact]
    public void TryParseRequest_PicksUpAnEditedMethodHeaderAndBody()
    {
        var raw = "PUT /edited HTTP/1.1\r\nHost: example.test\r\nX-Added: yes\r\n\r\nnew body";

        var ok = HttpMessageText.TryParseRequest(raw, out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("PUT", request!.Method);
        Assert.Equal("/edited", request.Target);
        Assert.Contains(request.Headers, h => h.Name == "X-Added" && h.Value == "yes");
        Assert.Equal("new body", Encoding.UTF8.GetString(request.Body));
    }

    [Fact]
    public void TryParseRequest_PreservesMultiLineBodyContent()
    {
        var raw = "POST /x HTTP/1.1\r\nHost: h\r\n\r\n{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}";

        var ok = HttpMessageText.TryParseRequest(raw, out var request, out _);

        Assert.True(ok);
        Assert.Equal("{\n  \"a\": 1,\n  \"b\": 2\n}", Encoding.UTF8.GetString(request!.Body));
    }

    [Fact]
    public void TryParseRequest_FailsOnAMalformedStartLine()
    {
        // Fewer than the two spaces a "METHOD /target HTTP/1.1" line needs
        // -- Split(' ', 3) can't even produce three parts from this, unlike
        // (deliberately, to match Http1MessageReader's own leniency about
        // what appears inside the target or version token) a line with
        // enough spaces but nonsense content.
        var ok = HttpMessageText.TryParseRequest("malformed\r\nHost: h\r\n\r\n", out var request, out var error);

        Assert.False(ok);
        Assert.Null(request);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParseRequest_FailsOnAHeaderLineWithNoColon()
    {
        var raw = "GET / HTTP/1.1\r\nHost example.test\r\n\r\n";

        var ok = HttpMessageText.TryParseRequest(raw, out var request, out var error);

        Assert.False(ok);
        Assert.Null(request);
        Assert.NotNull(error);
        Assert.Contains("Host example.test", error);
    }

    [Fact]
    public void Format_ThenTryParseResponse_RoundTripsAnUneditedResponse()
    {
        var original = new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "text/plain")], "hi"u8.ToArray());

        var text = HttpMessageText.Format(original);
        var ok = HttpMessageText.TryParseResponse(text, out var response, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(200, response!.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.Equal("hi", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void TryParseResponse_PicksUpAnEditedStatusCode()
    {
        var raw = "HTTP/1.1 404 Not Found\r\n\r\n";

        var ok = HttpMessageText.TryParseResponse(raw, out var response, out _);

        Assert.True(ok);
        Assert.Equal(404, response!.StatusCode);
        Assert.Equal("Not Found", response.ReasonPhrase);
    }

    [Fact]
    public void TryParseResponse_FailsWhenTheStatusCodeIsntNumeric()
    {
        var ok = HttpMessageText.TryParseResponse("HTTP/1.1 OK Fine\r\n\r\n", out var response, out var error);

        Assert.False(ok);
        Assert.Null(response);
        Assert.NotNull(error);
    }
}
