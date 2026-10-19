using System.IO.Compression;
using System.Text;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>The Statistics tab's numbers (<see cref="SessionStatistics"/>).</summary>
public class SessionStatisticsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static Session Add(
        SessionStore store, string host, int status, string contentType, byte[] body, double offsetMs, double totalMs,
        params (string Name, string Value)[] extraHeaders)
    {
        var request = new CapturedRequest("GET", "/", "HTTP/1.1", [("Host", host)], []);
        var headers = new List<(string Name, string Value)> { ("Content-Type", contentType) };
        headers.AddRange(extraHeaders);
        var response = new CapturedResponse("HTTP/1.1", status, "X", headers, body);
        return store.Add(host, Start.AddMilliseconds(offsetMs), request, response, null, "https",
            new SessionTimings { Blocked = 0, Send = 0, Wait = totalMs, Receive = 0 });
    }

    private static byte[] Gzip(string text)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return buffer.ToArray();
    }

    [Fact]
    public void SummarisesASetOfSessions()
    {
        var store = new SessionStore();
        var page = new string('x', 4000);
        Add(store, "www.example.test", 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page), 0, 100, ("Cache-Control", "no-cache"));
        Add(store, "cdn.example.test", 200, "application/javascript", Gzip(page), 50, 400, ("Content-Encoding", "gzip"), ("ETag", "\"1\""));
        Add(store, "cdn.example.test", 200, "image/png", new byte[2000], 60, 30);
        Add(store, "api.example.test", 404, "application/json", Encoding.UTF8.GetBytes("{}"), 70, 10);

        var stats = SessionStatistics.Compute(store.Snapshot());

        Assert.Equal(4, stats.Requests);
        Assert.Equal(TimeSpan.FromMilliseconds(450), stats.Span); // 50 + 400
        Assert.True(stats.DecodedResponseBytes > stats.ResponseBytes); // the gzipped script
        Assert.Equal([("2xx", 3), ("4xx", 1)], stats.ByStatus.Select(g => (g.Name, g.Count)));
        Assert.Equal("cdn.example.test", stats.ByHost[0].Name);
        Assert.Equal(2, stats.ByHost[0].Count);
        Assert.Contains(stats.ByKind, g => g.Name == "Script" && g.Count == 1);
        Assert.Contains(stats.ByKind, g => g.Name == "Image" && g.Count == 1);
        Assert.Equal(400, stats.Slowest[0].Timings!.Total);

        // The page is 4 KB of uncompressed HTML; the PNG has no caching headers.
        Assert.Equal("www.example.test", Assert.Single(stats.Uncompressed).Host);
        Assert.Equal("image/png", ContentDecoder.FindHeader(Assert.Single(stats.NotCacheable).Response.Headers, "Content-Type"));
    }

    [Theory]
    [InlineData("text/html", (int)ResourceKind.Document)]
    [InlineData("text/javascript; charset=utf-8", (int)ResourceKind.Script)]
    [InlineData("text/css", (int)ResourceKind.Stylesheet)]
    [InlineData("image/webp", (int)ResourceKind.Image)]
    [InlineData("font/woff2", (int)ResourceKind.Font)]
    [InlineData("application/problem+json", (int)ResourceKind.Data)]
    [InlineData("video/mp4", (int)ResourceKind.Media)]
    [InlineData("application/octet-stream", (int)ResourceKind.Other)]
    // The kind arrives as an int: ResourceKind is internal, and xUnit only
    // runs public methods, which can't take an internal type as a parameter.
    public void SortsResponsesIntoTheKindsDevToolsUses(string contentType, int expected)
    {
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", contentType)], []);

        Assert.Equal((ResourceKind)expected, SessionStatistics.KindOf(response));
    }

    [Fact]
    public void NoSessionsIsAllZeros()
    {
        var stats = SessionStatistics.Compute([]);

        Assert.Equal(0, stats.Requests);
        Assert.Empty(stats.ByKind);
    }
}
