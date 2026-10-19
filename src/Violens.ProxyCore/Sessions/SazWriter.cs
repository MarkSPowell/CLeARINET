using System.Globalization;
using System.IO.Compression;
using System.Text;
using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Sessions;

/// <summary>
/// Writes captured sessions out as a Fiddler-compatible .saz (Session
/// Archive Zip) file -- the actual Phase 1 exit criterion: "a core test
/// host captures a browser's HTTPS traffic and saves a SAZ that Fiddler
/// Classic opens."
///
/// Telerik doesn't publish the SAZ format. This is built against what the
/// open-source saz-tools parser (github.com/thomastay/saz-tools) expects,
/// since that project's whole job is reading real Fiddler-produced
/// archives: a zip with a raw/ folder holding three entries per session
/// (&lt;n&gt;_c.txt, &lt;n&gt;_s.txt, &lt;n&gt;_m.xml, session numbers
/// 1-based, no padding), each request/response file being an ordinary
/// HTTP/1.x message parseable by a standard HTTP reader, and _m.xml
/// following the &lt;Session&gt;/&lt;SessionTimers&gt;/&lt;SessionFlags&gt;
/// shape that library's Go structs decode. A root [Content_Types].xml is
/// included too, matching what real Fiddler output carries for zip-package
/// compatibility.
///
/// The SessionTimers attributes are filled in from
/// <see cref="Session.Timings"/>: each timestamp is the start plus the
/// phases before it, and DNSTime/TCPConnectTime/HTTPSHandshakeTime are in
/// whole milliseconds. A session with no timings gets its start time in
/// every timestamp and zeros. The exact date format Fiddler itself expects
/// here is unconfirmed (no real sample _m.xml was available to check
/// against) -- ISO 8601 round-trip format is used as the most broadly
/// parseable reasonable guess.
/// </summary>
public static class SazWriter
{
    public static void Write(string path, IReadOnlyList<Session> sessions)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        Write(stream, sessions);
    }

    public static void Write(Stream destination, IReadOnlyList<Session> sessions)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        WriteContentTypes(archive);

        foreach (var session in sessions)
        {
            WriteRequest(archive, session);
            WriteResponse(archive, session);
            WriteMetadata(archive, session);
        }
    }

    private static void WriteContentTypes(ZipArchive archive)
    {
        var entry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="txt" ContentType="text/plain" />
              <Default Extension="xml" ContentType="application/xml" />
            </Types>
            """);
    }

    private static void WriteRequest(ZipArchive archive, Session session)
    {
        var entry = archive.CreateEntry($"raw/{session.Id}_c.txt", CompressionLevel.Optimal);
        using var stream = entry.Open();
        // The body here is always what Http1MessageReader already decoded
        // -- never raw chunked-transfer-encoded wire bytes -- so
        // HttpMessageWriter rebuilds headers rather than replaying them
        // verbatim: Transfer-Encoding is dropped (there's nothing left to
        // de-chunk) and Content-Length is set to this decoded body's real
        // length, which may differ from whatever the original header said
        // (or said nothing at all, for a chunked message).
        // A plain HTTP request is written with its full URL in the request
        // line, as Fiddler Classic writes it; SazReader reads the scheme
        // back from that. HTTPS requests keep their path-only target.
        var request = string.Equals(session.Scheme, "http", StringComparison.OrdinalIgnoreCase) && session.Request.Target.StartsWith('/')
            ? session.Request with { Target = SessionUrl.Of(session) }
            : session.Request;
        stream.Write(HttpMessageWriter.BuildRequestBytes(request));
    }

    private static void WriteResponse(ZipArchive archive, Session session)
    {
        var entry = archive.CreateEntry($"raw/{session.Id}_s.txt", CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(HttpMessageWriter.BuildResponseBytes(session.Response));
    }

    private static void WriteMetadata(ZipArchive archive, Session session)
    {
        var entry = archive.CreateEntry($"raw/{session.Id}_m.xml", CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var t = session.Timings;
        static double Ms(double phase) => Math.Max(0, phase);
        var start = session.StartedAt;
        var requestRead = start.AddMilliseconds(t is null ? 0 : Ms(t.Blocked));
        var connected = requestRead.AddMilliseconds(t is null ? 0 : Ms(t.Dns) + Ms(t.Connect) + Ms(t.Ssl));
        var sent = connected.AddMilliseconds(t is null ? 0 : Ms(t.Send));
        var firstByte = sent.AddMilliseconds(t is null ? 0 : Ms(t.Wait));
        var done = firstByte.AddMilliseconds(t is null ? 0 : Ms(t.Receive));
        static string At(DateTimeOffset when) => when.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);
        static string Whole(double phase) => ((long)Math.Round(Math.Max(0, phase))).ToString(CultureInfo.InvariantCulture);

        writer.Write(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Session>
              <SessionTimers ClientConnected="{At(start)}" ClientBeginRequest="{At(start)}" GotRequestHeaders="{At(start)}" ClientDoneRequest="{At(requestRead)}" GatewayTime="0" DNSTime="{Whole(t?.Dns ?? 0)}" TCPConnectTime="{Whole(t?.Connect ?? 0)}" HTTPSHandshakeTime="{Whole(t?.Ssl ?? 0)}" ServerConnected="{At(connected)}" FiddlerBeginRequest="{At(connected)}" ServerGotRequest="{At(sent)}" ServerBeginResponse="{At(firstByte)}" GotResponseHeaders="{At(firstByte)}" ServerDoneResponse="{At(done)}" ClientBeginResponse="{At(firstByte)}" ClientDoneResponse="{At(done)}" />
              <SessionFlags />
            </Session>
            """);
    }
}
