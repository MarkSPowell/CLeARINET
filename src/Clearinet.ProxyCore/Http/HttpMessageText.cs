using System.Text;

namespace Clearinet.ProxyCore.Http;

/// <summary>
/// Converts a captured request/response to and from the raw-text form a
/// breakpoint's editor shows -- the same interaction model Fiddler
/// Classic's TextView uses: the whole message (start line, headers, body)
/// as one editable blob, rather than separate fields per header. See
/// Clearinet.ProxyCore.Breakpoints.PendingBreakpoint, which owns exactly
/// one round trip through this class per edit.
///
/// <see cref="Format"/> is built on <see cref="HttpMessageWriter"/>, so
/// what a user sees already has Content-Length fixed up and
/// Transfer-Encoding dropped, same as what would finally go on the wire.
/// <see cref="TryParseRequest"/>/<see cref="TryParseResponse"/>
/// deliberately do NOT re-fix Content-Length themselves -- whatever
/// headers the user typed are kept as-is, and it's <see cref="HttpMessageWriter"/>,
/// called later when the (possibly edited) message is actually sent, that
/// has the final say on Content-Length. That split is what avoids Fiddler
/// Classic's documented gotcha where an edit that left the raw text
/// technically malformed got silently dropped on "Run to Completion" --
/// here, a malformed edit is reported back as <c>error</c> instead, and
/// the caller decides what to do about it (see PendingBreakpoint.TryEdit).
///
/// A body is only round-tripped correctly through this class when it's
/// text (UTF-8 or a close enough ASCII subset) -- exactly the case raw-text
/// editing is for. A binary body still round-trips byte-for-byte AS LONG
/// AS it isn't edited (UTF-8 encode/decode is lossless for arbitrary bytes
/// interpreted leniently), but editing binary content in a text box was
/// never going to be practical; Hex-based breakpoint editing isn't in this
/// pass.
/// </summary>
public static class HttpMessageText
{
    public static string Format(CapturedRequest request) =>
        Encoding.UTF8.GetString(HttpMessageWriter.BuildRequestBytes(request));

    public static string Format(CapturedResponse response) =>
        Encoding.UTF8.GetString(HttpMessageWriter.BuildResponseBytes(response));

    public static bool TryParseRequest(string rawText, out CapturedRequest? request, out string? error)
    {
        if (!TrySplit(rawText, out var startLine, out var headerLines, out var bodyText, out error))
        {
            request = null;
            return false;
        }

        var parts = startLine.Split(' ', 3);
        if (parts.Length != 3)
        {
            request = null;
            error = $"Malformed request line: '{startLine}'. Expected 'METHOD /target HTTP/1.1'.";
            return false;
        }

        if (!TryParseHeaders(headerLines, out var headers, out error))
        {
            request = null;
            return false;
        }

        request = new CapturedRequest(parts[0], parts[1], parts[2], headers, Encoding.UTF8.GetBytes(bodyText));
        error = null;
        return true;
    }

    public static bool TryParseResponse(string rawText, out CapturedResponse? response, out string? error)
    {
        if (!TrySplit(rawText, out var startLine, out var headerLines, out var bodyText, out error))
        {
            response = null;
            return false;
        }

        var parts = startLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
        {
            response = null;
            error = $"Malformed status line: '{startLine}'. Expected 'HTTP/1.1 200 OK'.";
            return false;
        }

        if (!TryParseHeaders(headerLines, out var headers, out error))
        {
            response = null;
            return false;
        }

        var reason = parts.Length > 2 ? parts[2] : string.Empty;
        response = new CapturedResponse(parts[0], statusCode, reason, headers, Encoding.UTF8.GetBytes(bodyText));
        error = null;
        return true;
    }

    private static bool TrySplit(
        string rawText, out string startLine, out List<string> headerLines, out string bodyText, out string? error)
    {
        var normalized = rawText.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0].Length == 0)
        {
            startLine = string.Empty;
            headerLines = [];
            bodyText = string.Empty;
            error = "The message is empty -- it needs at least a start line.";
            return false;
        }

        startLine = lines[0];
        headerLines = [];
        var i = 1;
        while (i < lines.Length && lines[i].Length > 0)
        {
            headerLines.Add(lines[i]);
            i++;
        }

        // lines[i], if it exists, is the blank line separating headers from
        // the body -- everything after it, rejoined, is the body. Running
        // off the end with no blank line at all (headers but no body, and
        // no trailing blank line either) just means no body.
        bodyText = i + 1 < lines.Length ? string.Join('\n', lines[(i + 1)..]) : string.Empty;
        error = null;
        return true;
    }

    private static bool TryParseHeaders(
        List<string> headerLines, out List<(string Name, string Value)> headers, out string? error)
    {
        headers = new List<(string, string)>(headerLines.Count);
        foreach (var line in headerLines)
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                error = $"Malformed header line: '{line}'. Expected 'Name: value'.";
                return false;
            }

            headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        error = null;
        return true;
    }
}
