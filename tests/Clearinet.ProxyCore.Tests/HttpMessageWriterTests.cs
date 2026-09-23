using System.Text;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class HttpMessageWriterTests
{
    [Fact]
    public void BuildRequestBytes_ProducesAWellFormedStartLineHeadersAndBody()
    {
        var request = new CapturedRequest(
            "POST", "/foo?x=1", "HTTP/1.1", [("Host", "example.test"), ("Content-Length", "5")], "hello"u8.ToArray());

        var text = Encoding.ASCII.GetString(HttpMessageWriter.BuildRequestBytes(request));

        Assert.StartsWith("POST /foo?x=1 HTTP/1.1\r\n", text);
        Assert.Contains("Host: example.test\r\n", text);
        Assert.EndsWith("\r\n\r\nhello", text);
    }

    [Fact]
    public void BuildResponseBytes_RebuildsContentLengthAndDropsTransferEncodingForAChunkedBody()
    {
        var response = new CapturedResponse(
            "HTTP/1.1", 200, "OK", [("Transfer-Encoding", "chunked")], Encoding.ASCII.GetBytes("hello world"));

        var text = Encoding.ASCII.GetString(HttpMessageWriter.BuildResponseBytes(response));

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text);
        Assert.DoesNotContain("Transfer-Encoding", text);
        Assert.Contains("Content-Length: 11\r\n", text);
        Assert.EndsWith("hello world", text);
    }

    [Fact]
    public void BuildResponseBytes_FixesAStaleContentLengthRatherThanTrustingIt()
    {
        // Simulates a breakpoint edit that changed the body without
        // touching the Content-Length line -- exactly the gotcha the
        // Fiddler breakpoint research flagged (Classic silently drops such
        // an edit instead). CLeARINET always recomputes it here instead.
        var response = new CapturedResponse(
            "HTTP/1.1", 200, "OK", [("Content-Length", "999")], "short"u8.ToArray());

        var text = Encoding.ASCII.GetString(HttpMessageWriter.BuildResponseBytes(response));

        Assert.Contains("Content-Length: 5\r\n", text);
        Assert.DoesNotContain("Content-Length: 999", text);
    }

    [Fact]
    public void BuildResponseBytes_OmitsReasonPhraseWhenNoneIsGiven()
    {
        var response = new CapturedResponse("HTTP/1.1", 204, string.Empty, [], []);

        var text = Encoding.ASCII.GetString(HttpMessageWriter.BuildResponseBytes(response));

        Assert.StartsWith("HTTP/1.1 204\r\n", text);
    }

    [Fact]
    public async Task WriteRequestPreambleAsync_WritesHeadersVerbatimWithNoRewriting()
    {
        // Unlike BuildRequestBytes, the preamble writer is only ever paired
        // with Http1MessageReader.RelayBodyAsync immediately after -- the
        // body that's about to follow keeps whatever framing the original
        // request declared, so the headers announcing that framing have to
        // survive completely untouched, chunked or not.
        var request = new CapturedRequest(
            "POST", "/upload", "HTTP/1.1", [("Host", "example.test"), ("Transfer-Encoding", "chunked")], []);
        using var destination = new MemoryStream();

        await HttpMessageWriter.WriteRequestPreambleAsync(destination, request, CancellationToken.None);

        var text = Encoding.ASCII.GetString(destination.ToArray());
        Assert.StartsWith("POST /upload HTTP/1.1\r\n", text);
        Assert.Contains("Transfer-Encoding: chunked\r\n", text);
        Assert.DoesNotContain("Content-Length", text);
        Assert.EndsWith("\r\n\r\n", text);
    }

    [Fact]
    public async Task WriteResponsePreambleAsync_PreservesTheOriginalContentLengthRatherThanRebuildingIt()
    {
        // BuildResponseBytes would recompute this from the (here, still
        // empty -- unread) body; the preamble writer must not, since the
        // real body bytes are relayed separately right after this call.
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Length", "11")], []);
        using var destination = new MemoryStream();

        await HttpMessageWriter.WriteResponsePreambleAsync(destination, response, CancellationToken.None);

        var text = Encoding.ASCII.GetString(destination.ToArray());
        Assert.StartsWith("HTTP/1.1 200 OK\r\n", text);
        Assert.Contains("Content-Length: 11\r\n", text);
        Assert.EndsWith("\r\n\r\n", text);
    }
}
