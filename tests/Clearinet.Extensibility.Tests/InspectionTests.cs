using System.IO.Compression;
using System.Text;
using Clearinet.Extensibility.Inspection;
using Clearinet.Extensibility.Inspection.Inspectors;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;
using ZstdSharp;

namespace Clearinet.Extensibility.Tests;

public class InspectionTests
{
    [Fact]
    public void InspectorContext_FindHeader_IsCaseInsensitiveAndReturnsNullWhenAbsent()
    {
        var context = ContextFor(
            requestHeaders: [("Content-Type", "application/json"), ("X-Custom", "1")],
            requestBody: []);

        Assert.Equal("application/json", context.FindHeader("content-type"));
        Assert.Null(context.FindHeader("Authorization"));
    }

    [Fact]
    public void HeadersInspector_ReturnsEachHeaderAsARow()
    {
        var inspector = new HeadersInspector();
        var context = ContextFor(
            requestHeaders: [("Host", "example.test"), ("Accept", "*/*")],
            requestBody: []);

        Assert.True(inspector.CanInspect(context));
        var content = Assert.IsType<KeyValueContent>(inspector.Inspect(context));

        Assert.Equal(2, content.Rows.Count);
        Assert.Equal(new HeaderRow("Host", "example.test"), content.Rows[0]);
        Assert.Equal(new HeaderRow("Accept", "*/*"), content.Rows[1]);
    }

    [Fact]
    public void RawTextInspector_DecodesAnUncompressedUtf8Body()
    {
        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [],
            requestBody: Encoding.UTF8.GetBytes("hello world"));

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("hello world", content.Text);
    }

    [Fact]
    public void RawTextInspector_DecompressesAGzipBody()
    {
        var original = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        var compressed = Gzip(original);

        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "gzip")],
            requestBody: compressed);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("the quick brown fox jumps over the lazy dog", content.Text);
    }

    [Fact]
    public void RawTextInspector_ReturnsAnEmptyTextContentForAnEmptyBody()
    {
        var inspector = new RawTextInspector();
        var context = ContextFor(requestHeaders: [], requestBody: []);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal(string.Empty, content.Text);
    }

    [Fact]
    public void RawTextInspector_DecompressesAZstdBody()
    {
        var original = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        var compressed = Zstd(original);

        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "zstd")],
            requestBody: compressed);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("the quick brown fox jumps over the lazy dog", content.Text);
    }

    [Fact]
    public void RawTextInspector_ReturnsAnErrorWhenTheClaimedEncodingDoesNotActuallyDecompress()
    {
        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "gzip")],
            requestBody: "not actually gzip"u8.ToArray());

        Assert.IsType<ErrorContent>(inspector.Inspect(context));
    }

    [Theory]
    [InlineData("bzip2")]
    [InlineData("compress")]
    [InlineData("sdch")]
    public void RawTextInspector_ReturnsAnExplanatoryErrorForEachKnownButUnsupportedEncoding(string token)
    {
        // Bytes that don't decode as anything -- the point is that these
        // three never even attempt a decode, so what's actually in the body
        // doesn't matter to this test.
        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", token)],
            requestBody: [1, 2, 3, 4]);

        var content = Assert.IsType<ErrorContent>(inspector.Inspect(context));
        Assert.Contains(token, content.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RawTextInspector_DecodesAZlibWrappedDeflateBody()
    {
        // The common real-world case despite the confusing name -- most
        // servers sending Content-Encoding: deflate actually mean RFC 1950
        // (zlib-wrapped), not raw RFC 1951 DEFLATE.
        var original = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        var compressed = ZlibDeflate(original);

        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "deflate")],
            requestBody: compressed);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("the quick brown fox jumps over the lazy dog", content.Text);
    }

    [Fact]
    public void RawTextInspector_FallsBackToRawDeflateWhenTheBodyIsNotZlibWrapped()
    {
        // The historical IIS case -- Content-Encoding: deflate but the body
        // is actually raw RFC 1951 DEFLATE with no zlib header at all.
        var original = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        var compressed = RawDeflate(original);

        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "deflate")],
            requestBody: compressed);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("the quick brown fox jumps over the lazy dog", content.Text);
    }

    [Fact]
    public void RawTextInspector_UndoesAChainOfContentEncodingsInReverseApplicationOrder()
    {
        // "deflate, gzip" means deflate was applied first and gzip applied
        // on top of that -- so on the wire the body is gzip(deflate(original)),
        // and decoding has to undo gzip first, then deflate.
        var original = "the quick brown fox jumps over the lazy dog"u8.ToArray();
        var chained = Gzip(ZlibDeflate(original));

        var inspector = new RawTextInspector();
        var context = ContextFor(
            requestHeaders: [("Content-Encoding", "deflate, gzip")],
            requestBody: chained);

        var content = Assert.IsType<TextContent>(inspector.Inspect(context));
        Assert.Equal("the quick brown fox jumps over the lazy dog", content.Text);
    }

    [Fact]
    public void HexInspector_ReturnsTheBodyBytesUnmodified()
    {
        var inspector = new HexInspector();
        byte[] body = [0x00, 0xFF, 0x10, 0x20];
        var context = ContextFor(requestHeaders: [], requestBody: body);

        var content = Assert.IsType<HexContent>(inspector.Inspect(context));
        Assert.Equal(body, content.Bytes);
    }

    [Fact]
    public void InspectorRegistry_CreateDefault_ReturnsTheBuiltInsInDisplayOrder()
    {
        var registry = InspectorRegistry.CreateDefault();
        var context = ContextFor(requestHeaders: [], requestBody: []);

        var applicable = registry.GetApplicable(context);

        Assert.Equal(["Headers", "Raw", "Hex"], applicable.Select(i => i.DisplayName));
    }

    private static InspectorContext ContextFor(
        IReadOnlyList<(string Name, string Value)> requestHeaders, byte[] requestBody)
    {
        var request = new CapturedRequest("GET", "/", "HTTP/1.1", requestHeaders, requestBody);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [], []);
        var session = new Session(1, "example.test", DateTimeOffset.UtcNow, request, response);
        return new InspectorContext(session, InspectorSide.Request);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var destination = new MemoryStream();
        using (var gzip = new GZipStream(destination, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return destination.ToArray();
    }

    private static byte[] Zstd(byte[] data)
    {
        using var destination = new MemoryStream();
        using (var zstd = new CompressionStream(destination, leaveOpen: true))
        {
            zstd.Write(data);
        }

        return destination.ToArray();
    }

    private static byte[] ZlibDeflate(byte[] data)
    {
        using var destination = new MemoryStream();
        using (var deflate = new ZLibStream(destination, CompressionMode.Compress, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return destination.ToArray();
    }

    private static byte[] RawDeflate(byte[] data)
    {
        using var destination = new MemoryStream();
        using (var deflate = new DeflateStream(destination, CompressionMode.Compress, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return destination.ToArray();
    }
}
