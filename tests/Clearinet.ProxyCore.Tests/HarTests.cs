using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>HAR export (<see cref="HarWriter"/>) and import (<see cref="HarReader"/>).</summary>
public class HarTests
{
    private static readonly DateTimeOffset Started = new(2026, 9, 26, 10, 30, 0, TimeSpan.Zero);

    private static byte[] Gzip(string text)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(text));
        }

        return buffer.ToArray();
    }

    private static Session JsonSession()
    {
        var store = new SessionStore();
        var request = new CapturedRequest(
            "POST",
            "/api/items?page=2&q=a+b",
            "HTTP/1.1",
            [("Host", "api.example.test:8443"), ("Content-Type", "application/json"), ("Cookie", "sid=abc; theme=dark")],
            Encoding.UTF8.GetBytes("{\"name\":\"x\"}"));
        var compressed = Gzip("{\"ok\":true}");
        var response = new CapturedResponse(
            "HTTP/1.1",
            201,
            "Created",
            [("Content-Type", "application/json"), ("Content-Encoding", "gzip"), ("Set-Cookie", "sid=new; Path=/; HttpOnly")],
            compressed);
        return store.Add("api.example.test", Started, request, response, new Dictionary<string, string> { ["ui-comments"] = "checked" });
    }

    private static JsonDocument Export(params Session[] sessions)
    {
        using var buffer = new MemoryStream();
        HarWriter.Write(buffer, sessions);
        return JsonDocument.Parse(buffer.ToArray());
    }

    [Fact]
    public void ExportsAnEntryAsHar12()
    {
        using var har = Export(JsonSession());
        var log = har.RootElement.GetProperty("log");
        Assert.Equal("1.2", log.GetProperty("version").GetString());
        Assert.Equal("CLeARINET", log.GetProperty("creator").GetProperty("name").GetString());

        var entry = Assert.Single(log.GetProperty("entries").EnumerateArray());
        Assert.Equal("checked", entry.GetProperty("comment").GetString());
        Assert.Equal(Started, entry.GetProperty("startedDateTime").GetDateTimeOffset());

        var request = entry.GetProperty("request");
        Assert.Equal("POST", request.GetProperty("method").GetString());
        Assert.Equal("https://api.example.test:8443/api/items?page=2&q=a+b", request.GetProperty("url").GetString());
        Assert.Equal("{\"name\":\"x\"}", request.GetProperty("postData").GetProperty("text").GetString());
        Assert.Equal(2, request.GetProperty("queryString").GetArrayLength());
        Assert.Equal("a b", request.GetProperty("queryString")[1].GetProperty("value").GetString());
        Assert.Equal(2, request.GetProperty("cookies").GetArrayLength());

        var response = entry.GetProperty("response");
        Assert.Equal(201, response.GetProperty("status").GetInt32());
        var content = response.GetProperty("content");
        Assert.Equal("{\"ok\":true}", content.GetProperty("text").GetString());
        Assert.False(content.TryGetProperty("encoding", out _));
        Assert.Equal(11, content.GetProperty("size").GetInt32());
        Assert.True(content.TryGetProperty("compression", out _));
        var cookie = Assert.Single(response.GetProperty("cookies").EnumerateArray());
        Assert.True(cookie.GetProperty("httpOnly").GetBoolean());
    }

    [Fact]
    public void BinaryBodiesAreBase64()
    {
        var store = new SessionStore();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0xFF };
        var session = store.Add(
            "img.example.test",
            Started,
            new CapturedRequest("GET", "/a.png", "HTTP/1.1", [("Host", "img.example.test")], []),
            new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "image/png")], png));

        using var har = Export(session);
        var content = har.RootElement.GetProperty("log").GetProperty("entries")[0].GetProperty("response").GetProperty("content");

        Assert.Equal("base64", content.GetProperty("encoding").GetString());
        Assert.Equal(png, Convert.FromBase64String(content.GetProperty("text").GetString()!));
    }

    [Fact]
    public async Task RoundTripsThroughImport()
    {
        using var buffer = new MemoryStream();
        HarWriter.Write(buffer, [JsonSession()]);
        buffer.Position = 0;
        var store = new SessionStore();

        var result = await HarReader.ImportAsync(buffer, store);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Skipped);
        var session = Assert.Single(store.Snapshot());
        Assert.Equal("api.example.test", session.Host);
        Assert.Equal(Started, session.StartedAt);
        Assert.Equal("/api/items?page=2&q=a+b", session.Request.Target);
        Assert.Equal("{\"name\":\"x\"}", Encoding.UTF8.GetString(session.Request.Body));
        Assert.Equal(201, session.Response.StatusCode);
        Assert.Equal("Created", session.Response.ReasonPhrase);
        // HAR bodies are decoded, so the encoding header no longer applies.
        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(session.Response.Body));
        Assert.Null(ContentDecoder.FindHeader(session.Response.Headers, "Content-Encoding"));
        Assert.Equal("11", ContentDecoder.FindHeader(session.Response.Headers, "Content-Length"));
        Assert.Equal("checked", session.Flags!["ui-comments"]);
    }

    [Fact]
    public async Task ImportsABrowserStyleHar()
    {
        const string har = """
            {"log":{"version":"1.2","creator":{"name":"WebInspector","version":"537.36"},"entries":[
              {"startedDateTime":"2026-09-26T10:30:00.000Z","time":12.5,
               "request":{"method":"GET","url":"https://www.example.test/index.html","httpVersion":"h2",
                 "headers":[{"name":":authority","value":"www.example.test"},{"name":"accept","value":"text/html"}],
                 "queryString":[],"cookies":[],"headersSize":-1,"bodySize":0},
               "response":{"status":200,"statusText":"","httpVersion":"h2",
                 "headers":[{"name":"content-type","value":"text/html"},{"name":"content-encoding","value":"br"}],
                 "cookies":[],"content":{"size":5,"mimeType":"text/html","text":"hello"},
                 "redirectURL":"","headersSize":-1,"bodySize":-1},
               "cache":{},"timings":{"send":0,"wait":10,"receive":2.5}},
              {"request":{"method":"GET","url":"not a url"},"response":{"status":200}}
            ]}}
            """;
        var store = new SessionStore();

        var result = await HarReader.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(har)), store);

        Assert.Equal(1, result.Imported);
        Assert.Single(result.Skipped);
        var session = Assert.Single(store.Snapshot());
        Assert.Equal("HTTP/2", session.Request.HttpVersion);
        Assert.Equal("www.example.test", ContentDecoder.FindHeader(session.Request.Headers, "Host"));
        Assert.DoesNotContain(session.Request.Headers, h => h.Name.StartsWith(':'));
        Assert.Equal("hello", Encoding.UTF8.GetString(session.Response.Body));
        Assert.Null(ContentDecoder.FindHeader(session.Response.Headers, "content-encoding"));
    }

    [Fact]
    public async Task RejectsSomethingThatIsNotHar()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            HarReader.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"hello\":1}")), new SessionStore()));
    }
}
