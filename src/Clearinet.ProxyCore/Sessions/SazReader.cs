using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// Reads a Fiddler-compatible .saz file back into <see cref="SessionStore"/>
/// -- the inverse of <see cref="SazWriter"/>, and the other half of the
/// "import and export of .saz traffic captures" requirement SazWriter's own
/// remarks quote. Without this, CLeARINET could only ever save its own
/// captures, never open one someone else sent, one attached to a bug
/// report, or one saved from Fiddler Classic itself.
///
/// Parses each session's raw request/response text with
/// <see cref="Http1MessageReader"/> -- the exact same byte-level parser
/// live capture uses to read messages off the wire -- rather than
/// <c>HttpMessageText</c>'s UTF-8 text round-trip (built for a person
/// hand-editing a breakpoint, and lossy for a binary body -- see that
/// class's own remarks). Reading a zip entry as a <see cref="Stream"/> and
/// handing it straight to the same reader real traffic goes through means
/// a captured image or other binary body survives export-then-import
/// byte-for-byte.
///
/// Imported sessions are added through <see cref="SessionStore.Add"/> --
/// the exact same path live capture uses -- rather than handed back as a
/// list for a caller to insert some other way. That means an import gets
/// fresh, store-assigned <see cref="Session.Id"/> values (an archive's own
/// internal numbering is never reused, and can't collide with whatever's
/// already been captured this run) and fires
/// <see cref="SessionStore.SessionAdded"/> once per session, so a live UI
/// picks up imported sessions through the exact same subscription it
/// already has for freshly captured ones -- no separate "import" code path
/// for a host application to maintain.
///
/// One malformed or incomplete session in an archive doesn't fail the
/// whole import: each session is parsed independently, and a failure is
/// recorded in <see cref="SazImportResult.Skipped"/> instead of thrown --
/// the same reasoning <c>MainWindowViewModel.AddInspectorTabs</c> applies
/// per-inspector, applied here per-session.
/// </summary>
public static class SazReader
{
    public static Task<SazImportResult> ImportAsync(
        string path, SessionStore sessionStore, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        return ImportAsync(stream, sessionStore, cancellationToken);
    }

    public static async Task<SazImportResult> ImportAsync(
        Stream source, SessionStore sessionStore, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

        var imported = 0;
        var skipped = new List<string>();

        foreach (var number in FindSessionNumbers(archive))
        {
            try
            {
                await ImportOneAsync(archive, number, sessionStore, cancellationToken);
                imported++;
            }
            catch (Exception ex)
            {
                skipped.Add($"#{number}: {ex.Message}");
            }
        }

        return new SazImportResult(imported, skipped);
    }

    private static async Task ImportOneAsync(
        ZipArchive archive, int number, SessionStore sessionStore, CancellationToken cancellationToken)
    {
        var requestEntry = archive.GetEntry($"raw/{number}_c.txt")
            ?? throw new InvalidDataException($"missing request file (raw/{number}_c.txt)");
        var responseEntry = archive.GetEntry($"raw/{number}_s.txt")
            ?? throw new InvalidDataException($"missing response file (raw/{number}_s.txt)");

        CapturedRequest request;
        await using (var requestStream = requestEntry.Open())
        {
            request = await Http1MessageReader.ReadRequestAsync(requestStream, cancellationToken)
                ?? throw new InvalidDataException("request file was empty");
        }

        CapturedResponse response;
        await using (var responseStream = responseEntry.Open())
        {
            // isResponseToHeadRequest: false is always safe here -- SazWriter
            // only ever omits Content-Length when the body is genuinely
            // empty (see its own remarks on always rebuilding it from the
            // real body length), so there's no ambiguous framing this could
            // misread regardless of what the original request method was.
            response = await Http1MessageReader.ReadResponseAsync(responseStream, isResponseToHeadRequest: false, cancellationToken)
                ?? throw new InvalidDataException("response file was empty");
        }

        var host = ExtractHost(request.Headers);
        var startedAt = TryReadTimestamp(archive.GetEntry($"raw/{number}_m.xml")) ?? DateTimeOffset.Now;

        sessionStore.Add(host, startedAt, request, response);
    }

    private static List<int> FindSessionNumbers(ZipArchive archive)
    {
        const string prefix = "raw/";
        const string suffix = "_c.txt";

        var numbers = new List<int>();
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
                entry.FullName.EndsWith(suffix, StringComparison.Ordinal))
            {
                var numberText = entry.FullName[prefix.Length..^suffix.Length];
                if (int.TryParse(numberText, out var number))
                {
                    numbers.Add(number);
                }
            }
        }

        numbers.Sort();
        return numbers;
    }

    /// <summary>
    /// SazWriter's own _m.xml doesn't carry a Host field -- only
    /// SessionTimers/SessionFlags -- so the host comes from the request's
    /// own Host header instead, the same header every real HTTP/1.1 client
    /// already sends. A Host header can carry an explicit port
    /// ("example.com:8443"); <see cref="Session.Host"/> elsewhere in this
    /// codebase is always a bare hostname (see SessionQuery.Url's own
    /// remarks on this proxy's HTTPS-only, port-443-assumed design), so any
    /// port suffix is stripped to match rather than introduce a
    /// "host:port" shape this codebase doesn't otherwise use.
    /// </summary>
    private static string ExtractHost(IReadOnlyList<(string Name, string Value)> headers)
    {
        foreach (var (name, value) in headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = value.LastIndexOf(':');
                return colonIndex > 0 ? value[..colonIndex] : value;
            }
        }

        // No Host header at all -- legal for a very old HTTP/1.0 request,
        // but every modern capture (and everything CLeARINET itself
        // produces) has one. A missing display value shouldn't fail the
        // whole session over it.
        return "(unknown host)";
    }

    /// <summary>
    /// Best-effort only, and expected to come up empty for a real Fiddler
    /// Classic capture -- see <see cref="SazWriter"/>'s own remarks on why
    /// the exact _m.xml timestamp format a real Fiddler installation
    /// expects is unconfirmed. A session with no readable timestamp still
    /// imports fine; it just gets "now" instead of when it was originally
    /// captured.
    /// </summary>
    private static DateTimeOffset? TryReadTimestamp(ZipArchiveEntry? metadataEntry)
    {
        if (metadataEntry is null)
        {
            return null;
        }

        try
        {
            using var stream = metadataEntry.Open();
            var doc = XDocument.Load(stream);
            var text = doc.Root?.Element("SessionTimers")?.Attribute("ClientBeginRequest")?.Value;
            return text is not null
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// The outcome of an import: how many sessions made it into
/// <see cref="SessionStore"/>, and a human-readable reason for each one
/// that didn't.
/// </summary>
public sealed record SazImportResult(int Imported, IReadOnlyList<string> Skipped);
