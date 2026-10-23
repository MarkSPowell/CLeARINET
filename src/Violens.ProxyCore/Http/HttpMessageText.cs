using System.Text;

namespace Violens.ProxyCore.Http;

/// <summary>
/// Converts a message to and from the raw text a breakpoint editor shows,
/// like Fiddler Classic's TextView. Parsing keeps the headers as typed;
/// <see cref="HttpMessageWriter"/> fixes Content-Length when sending, and a
/// malformed edit is reported rather than dropped. Only text bodies are
/// meant to be edited this way.
/// </summary>
public static class HttpMessageText
{
    public static string Format(CapturedRequest request) =>
        Encoding.UTF8.GetString(HttpMessageWriter.BuildRequestBytes(request));

    /// <summary>
    /// The request line and headers only (ending with the blank line), as
    /// <see cref="Format(CapturedRequest)"/> would show them: Content-Length
    /// is the body's real length. For showing a request whose body isn't
    /// text, so the body can be edited some other way.
    /// </summary>
    public static string FormatHead(CapturedRequest request)
    {
        var bytes = HttpMessageWriter.BuildRequestBytes(request);
        var end = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? bytes.Length : end + 4);
    }

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

        // Everything after the blank line is the body.
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
