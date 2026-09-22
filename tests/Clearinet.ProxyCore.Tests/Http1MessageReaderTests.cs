using System.Text;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class Http1MessageReaderTests
{
    [Fact]
    public async Task ReadRequestAsync_ParsesStartLineHeadersAndContentLengthBody()
    {
        var raw = "POST /foo?x=1 HTTP/1.1\r\nHost: example.test\r\nContent-Length: 5\r\n\r\nhello";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var request = await Http1MessageReader.ReadRequestAsync(source, relay, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("POST", request!.Method);
        Assert.Equal("/foo?x=1", request.Target);
        Assert.Equal("HTTP/1.1", request.HttpVersion);
        Assert.Contains(request.Headers, h => h.Name == "Host" && h.Value == "example.test");
        Assert.Contains(request.Headers, h => h.Name == "Content-Length" && h.Value == "5");
        Assert.Equal("hello", Encoding.ASCII.GetString(request.Body));

        // The relay side must see the exact original bytes, unmodified --
        // that's what lets the upstream/client on the other end of the
        // tunnel behave exactly as if this reader weren't there.
        Assert.Equal(raw, Encoding.ASCII.GetString(relay.ToArray()));
    }

    [Fact]
    public async Task ReadRequestAsync_ReturnsNullWhenConnectionClosesBeforeAnotherRequest()
    {
        using var source = new MemoryStream();
        using var relay = new MemoryStream();

        var request = await Http1MessageReader.ReadRequestAsync(source, relay, CancellationToken.None);

        Assert.Null(request);
    }

    [Fact]
    public async Task ReadRequestAsync_RequestWithNoBodyHeadersHasEmptyBody()
    {
        var raw = "GET /foo HTTP/1.1\r\nHost: example.test\r\n\r\n";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var request = await Http1MessageReader.ReadRequestAsync(source, relay, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Empty(request!.Body);
    }

    [Fact]
    public async Task ReadRequestAsync_DecodesChunkedBodyAndRelaysTheOriginalChunkedBytes()
    {
        var raw = "POST /x HTTP/1.1\r\nHost: h\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var request = await Http1MessageReader.ReadRequestAsync(source, relay, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("hello world", Encoding.ASCII.GetString(request!.Body));
        Assert.Equal(raw, Encoding.ASCII.GetString(relay.ToArray()));
    }

    [Fact]
    public async Task ReadResponseAsync_ParsesStatusLineHeadersAndBody()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nhi";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var response = await Http1MessageReader.ReadResponseAsync(
            source, relay, isResponseToHeadRequest: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(200, response!.StatusCode);
        Assert.Equal("OK", response.ReasonPhrase);
        Assert.Equal("hi", Encoding.ASCII.GetString(response.Body));
    }

    [Fact]
    public async Task ReadResponseAsync_A204NeverReadsABodyEvenIfOneIsClaimed()
    {
        // 204 responses aren't allowed to carry a body at all; a
        // Content-Length here would be a malformed server, and trying to
        // honor it would just hang waiting for bytes that never arrive.
        var raw = "HTTP/1.1 204 No Content\r\n\r\n";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var response = await Http1MessageReader.ReadResponseAsync(
            source, relay, isResponseToHeadRequest: false, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Empty(response!.Body);
    }

    [Fact]
    public async Task ReadResponseAsync_ResponseToAHeadRequestNeverReadsABody()
    {
        // The server would claim Content-Length: 100 but never actually
        // send those bytes for a HEAD request -- reading them anyway would
        // hang forever.
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 100\r\n\r\n";
        using var source = StreamOf(raw);
        using var relay = new MemoryStream();

        var response = await Http1MessageReader.ReadResponseAsync(
            source, relay, isResponseToHeadRequest: true, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Empty(response!.Body);
    }

    [Fact]
    public async Task ReadResponseAsync_ReturnsNullWhenUpstreamClosesWithoutAnswering()
    {
        using var source = new MemoryStream();
        using var relay = new MemoryStream();

        var response = await Http1MessageReader.ReadResponseAsync(
            source, relay, isResponseToHeadRequest: false, CancellationToken.None);

        Assert.Null(response);
    }

    private static MemoryStream StreamOf(string text) => new(Encoding.ASCII.GetBytes(text));
}
