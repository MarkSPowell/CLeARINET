using System.IO.Compression;
using System.Text;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class SazReaderTests
{
    [Fact]
    public async Task ImportAsync_RoundTripsWhatSazWriterWrote()
    {
        var request = new CapturedRequest(
            "GET",
            "/foo?x=1",
            "HTTP/1.1",
            [("Host", "example.test")],
            []);
        var response = new CapturedResponse(
            "HTTP/1.1",
            200,
            "OK",
            [("Content-Type", "text/plain")],
            Encoding.ASCII.GetBytes("hello"));
        var written = new Session(1, "example.test", DateTimeOffset.UtcNow, request, response);

        using var zipStream = new MemoryStream();
        SazWriter.Write(zipStream, [written]);
        zipStream.Position = 0;

        var store = new SessionStore();
        var result = await SazReader.ImportAsync(zipStream, store);

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Skipped);

        var imported = Assert.Single(store.Snapshot());
        Assert.Equal("example.test", imported.Host);
        Assert.Equal("GET", imported.Request.Method);
        Assert.Equal("/foo?x=1", imported.Request.Target);
        Assert.Equal(200, imported.Response.StatusCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(imported.Response.Body));
    }

    [Fact]
    public async Task ImportAsync_AssignsFreshIdsRatherThanReusingTheArchivesOwnNumbering()
    {
        // SazWriter numbers sessions from the list position, so writing two
        // sessions here already produces archive numbers 1 and 2 -- the
        // interesting case is a *pre-populated* SessionStore, so the
        // imported sessions can't just reuse 1 and 2 without colliding with
        // what's already there.
        // Two distinct Host headers, not one CapturedRequest reused via
        // `with` -- SazReader derives a session's Host purely from its
        // request's Host header (see ExtractHost's own remarks; a SAZ
        // file's _m.xml never carries one), so two sessions that differ
        // only in Session.Host and not in their Host header would come
        // back from import indistinguishable, which would defeat the
        // point of this test.
        var requestA = new CapturedRequest("GET", "/a", "HTTP/1.1", [("Host", "a.test")], []);
        var requestB = new CapturedRequest("GET", "/b", "HTTP/1.1", [("Host", "b.test")], []);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [], []);
        var sessions = new[]
        {
            new Session(1, "a.test", DateTimeOffset.UtcNow, requestA, response),
            new Session(2, "b.test", DateTimeOffset.UtcNow, requestB, response),
        };

        using var zipStream = new MemoryStream();
        SazWriter.Write(zipStream, sessions);
        zipStream.Position = 0;

        var store = new SessionStore();
        store.Add("already-here.test", DateTimeOffset.UtcNow, requestA, response); // takes id 1

        var result = await SazReader.ImportAsync(zipStream, store);

        Assert.Equal(2, result.Imported);
        var all = store.Snapshot();
        Assert.Equal(3, all.Count);
        // Fresh, store-assigned, still increasing -- never a collision with
        // the pre-existing session's id, and never the archive's own 1/2.
        Assert.Equal([1, 2, 3], all.Select(s => s.Id));
        Assert.Equal("already-here.test", all[0].Host);
        Assert.Equal("a.test", all[1].Host);
        Assert.Equal("b.test", all[2].Host);
    }

    [Fact]
    public async Task ImportAsync_SkipsAMalformedSessionButStillImportsTheRest()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteRawEntry(archive, "raw/1_c.txt", "GET /one HTTP/1.1\r\nHost: good.test\r\n\r\n");
            WriteRawEntry(archive, "raw/1_s.txt", "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");

            // Session 2 has a request file but no response file -- the
            // per-session failure this test is really after.
            WriteRawEntry(archive, "raw/2_c.txt", "GET /two HTTP/1.1\r\nHost: broken.test\r\n\r\n");

            WriteRawEntry(archive, "raw/3_c.txt", "GET /three HTTP/1.1\r\nHost: good.test\r\n\r\n");
            WriteRawEntry(archive, "raw/3_s.txt", "HTTP/1.1 204 No Content\r\n\r\n");
        }

        zipStream.Position = 0;
        var store = new SessionStore();
        var result = await SazReader.ImportAsync(zipStream, store);

        Assert.Equal(2, result.Imported);
        var skipped = Assert.Single(result.Skipped);
        Assert.Contains("#2", skipped);

        var imported = store.Snapshot();
        Assert.Equal(2, imported.Count);
        Assert.Equal("/one", imported[0].Request.Target);
        Assert.Equal("/three", imported[1].Request.Target);
    }

    [Fact]
    public async Task ImportAsync_PreservesABinaryBodyByteForByte()
    {
        var binaryBody = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var request = new CapturedRequest("GET", "/image.png", "HTTP/1.1", [("Host", "example.test")], []);
        var response = new CapturedResponse(
            "HTTP/1.1",
            200,
            "OK",
            [("Content-Type", "image/png")],
            binaryBody);
        var written = new Session(1, "example.test", DateTimeOffset.UtcNow, request, response);

        using var zipStream = new MemoryStream();
        SazWriter.Write(zipStream, [written]);
        zipStream.Position = 0;

        var store = new SessionStore();
        await SazReader.ImportAsync(zipStream, store);

        var imported = Assert.Single(store.Snapshot());
        Assert.Equal(binaryBody, imported.Response.Body);
    }

    [Fact]
    public async Task ImportAsync_StripsThePortFromTheHostHeader()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteRawEntry(archive, "raw/1_c.txt", "GET / HTTP/1.1\r\nHost: example.test:8443\r\n\r\n");
            WriteRawEntry(archive, "raw/1_s.txt", "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        }

        zipStream.Position = 0;
        var store = new SessionStore();
        var result = await SazReader.ImportAsync(zipStream, store);

        Assert.Equal(1, result.Imported);
        var imported = Assert.Single(store.Snapshot());
        // Session.Host is always a bare hostname elsewhere in this codebase
        // (see SazReader.ExtractHost's own remarks) -- the port from the
        // Host header shouldn't leak through into a "host:port" shape this
        // codebase doesn't otherwise use.
        Assert.Equal("example.test", imported.Host);
    }

    [Fact]
    public async Task ImportAsync_FallsBackToUnknownHostWhenThereIsNoHostHeaderAtAll()
    {
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteRawEntry(archive, "raw/1_c.txt", "GET / HTTP/1.0\r\n\r\n");
            WriteRawEntry(archive, "raw/1_s.txt", "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        }

        zipStream.Position = 0;
        var store = new SessionStore();
        await SazReader.ImportAsync(zipStream, store);

        var imported = Assert.Single(store.Snapshot());
        Assert.Equal("(unknown host)", imported.Host);
    }

    private static void WriteRawEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.ASCII.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
