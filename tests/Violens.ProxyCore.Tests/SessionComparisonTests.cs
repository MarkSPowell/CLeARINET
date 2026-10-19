using System.IO.Compression;
using System.Text;
using Violens.ProxyCore.Compare;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>Compare Sessions (<see cref="SessionComparison"/>).</summary>
public class SessionComparisonTests
{
    private static Session Exchange(
        string target = "/api/items?page=1",
        string json = "{\"id\":1,\"name\":\"alpha\"}",
        bool gzip = false,
        (string, string)[]? extraRequestHeaders = null,
        string contentType = "application/json")
    {
        var requestHeaders = new List<(string, string)> { ("Host", "api.example.test"), ("Accept", "application/json") };
        requestHeaders.AddRange(extraRequestHeaders ?? []);
        var request = new CapturedRequest("GET", target, "HTTP/1.1", requestHeaders, []);

        var body = Encoding.UTF8.GetBytes(json);
        var responseHeaders = new List<(string, string)> { ("Content-Type", contentType) };
        if (gzip)
        {
            using var compressed = new MemoryStream();
            using (var stream = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                stream.Write(body);
            }

            body = compressed.ToArray();
            responseHeaders.Add(("Content-Encoding", "gzip"));
        }

        var response = new CapturedResponse("HTTP/1.1", 200, "OK", responseHeaders, body);
        return new SessionStore().Add("api.example.test", DateTimeOffset.UtcNow, request, response);
    }

    [Fact]
    public void TheSameExchangeIsIdentical()
    {
        var result = SessionComparison.Compare(Exchange(), Exchange());

        Assert.Equal(0, result.Differences);
    }

    [Fact]
    public void TheRequestLineShowsTheFullUrl()
    {
        var result = SessionComparison.Compare(Exchange(), Exchange(target: "/api/items?page=2"));

        var row = Assert.Single(result.Request.Rows, r => r.Kind == DiffKind.Changed);
        Assert.Equal("GET https://api.example.test/api/items?page=1 HTTP/1.1", row.Left);
        Assert.Equal("GET https://api.example.test/api/items?page=2 HTTP/1.1", row.Right);
        Assert.True(result.Response.Identical);
    }

    [Fact]
    public void JsonIsComparedOneValuePerLine()
    {
        var result = SessionComparison.Compare(Exchange(), Exchange(json: "{\"id\":1,\"name\":\"beta\"}"));

        var row = Assert.Single(result.Response.Rows, r => r.Kind == DiffKind.Changed);
        Assert.Equal("  \"name\": \"alpha\"", row.Left);
        Assert.Equal("  \"name\": \"beta\"", row.Right);
    }

    [Fact]
    public void WithoutFormattingJsonStaysOnOneLine()
    {
        var options = new SessionCompareOptions(FormatBodies: false);

        var text = SessionComparison.ResponseText(Exchange().Response, options);

        Assert.EndsWith("\n\n{\"id\":1,\"name\":\"alpha\"}", text);
    }

    [Fact]
    public void CompressedBodiesAreComparedDecoded()
    {
        var result = SessionComparison.Compare(Exchange(), Exchange(gzip: true));

        // Only the Content-Encoding header differs, not the content.
        var row = Assert.Single(result.Response.Rows, r => r.Kind != DiffKind.Same);
        Assert.Equal(DiffKind.Added, row.Kind);
        Assert.Equal("Content-Encoding: gzip", row.Right);
    }

    [Fact]
    public void WithoutDecodingCompressedBytesAreShownAsBinary()
    {
        var text = SessionComparison.ResponseText(Exchange(gzip: true).Response, new SessionCompareOptions(DecodeBodies: false));

        Assert.Contains("[Binary body: ", text);
        Assert.Contains("1F 8B", text);
    }

    [Fact]
    public void SortingHeadersIgnoresTheirOrder()
    {
        var left = Exchange(extraRequestHeaders: [("X-A", "1"), ("X-B", "2")]);
        var right = Exchange(extraRequestHeaders: [("X-B", "2"), ("X-A", "1")]);

        Assert.False(SessionComparison.Compare(left, right).Request.Identical);
        Assert.True(SessionComparison.Compare(left, right, new SessionCompareOptions(SortHeaders: true)).Request.Identical);
    }

    [Fact]
    public void FormsAreComparedOneFieldPerLine()
    {
        Assert.Equal("a=1\nb=two\nc=", SessionComparison.Format("a=1&b=two&c=", "application/x-www-form-urlencoded"));
    }

    [Fact]
    public void InvalidJsonIsLeftAsItIs()
    {
        Assert.Equal("{not json", SessionComparison.Format("{not json", "application/json"));
    }

    [Fact]
    public void LargeBinaryBodiesAreComparedBySizeAndHash()
    {
        var bytes = new byte[SessionComparison.MaxHexDumpBytes + 1];
        bytes[0] = 0xFF;

        var text = SessionComparison.BodyText(bytes, [("Content-Type", "image/png")], new SessionCompareOptions());

        Assert.StartsWith("[Binary body: ", text);
        Assert.DoesNotContain("\n", text);
    }

    [Fact]
    public void HexDumpShowsOffsetsBytesAndText()
    {
        var dump = SessionComparison.HexDump(Encoding.ASCII.GetBytes("Hello, world!\0\x01xyz"));

        var lines = dump.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("00000000  48 65 6C 6C 6F 2C 20 77 6F 72 6C 64 21 00 01 78  Hello, world!..x", lines[0]);
        Assert.StartsWith("00000010  79 7A ", lines[1]);
        Assert.EndsWith(" yz", lines[1]);
    }

    [Fact]
    public void UntypedUtf8BodiesAreText()
    {
        var text = SessionComparison.BodyText(Encoding.UTF8.GetBytes("plain words"), [], new SessionCompareOptions());

        Assert.Equal("plain words", text);
    }
}
