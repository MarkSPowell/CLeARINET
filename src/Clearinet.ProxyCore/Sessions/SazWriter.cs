using System.Globalization;
using System.IO.Compression;
using System.Text;
using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

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
/// One honest gap: the SessionTimers attributes are real Fiddler fields
/// (DNS time, TCP connect time, handshake time, and so on), but this spike
/// only has one timestamp per session -- when the request started -- not
/// the per-phase timing Fiddler itself records. Every timer attribute is
/// set to that one timestamp rather than omitted, so the element is at
/// least well-formed, but the Timers tab in a real Fiddler Classic install
/// may not show anything meaningful yet. That's a known, scoped gap, not
/// an oversight -- per-phase timing is Phase 2 work once there's a session
/// pipeline to hang it on. The exact date format Fiddler itself expects
/// here is also unconfirmed (no real sample _m.xml was available to check
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
        stream.Write(HttpMessageWriter.BuildRequestBytes(session.Request));
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

        var timestamp = session.StartedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);
        writer.Write(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Session>
              <SessionTimers ClientConnected="{timestamp}" ClientBeginRequest="{timestamp}" GotRequestHeaders="{timestamp}" ClientDoneRequest="{timestamp}" GatewayTime="0" DNSTime="0" TCPConnectTime="0" HTTPSHandshakeTime="0" ServerConnected="{timestamp}" FiddlerBeginRequest="{timestamp}" ServerGotRequest="{timestamp}" ServerBeginResponse="{timestamp}" GotResponseHeaders="{timestamp}" ServerDoneResponse="{timestamp}" ClientBeginResponse="{timestamp}" ClientDoneResponse="{timestamp}" />
              <SessionFlags />
            </Session>
            """);
    }
}
