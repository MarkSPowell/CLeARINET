namespace Clearinet.CompatShim;

/// <summary>
/// Fiddler's <c>Parser</c>: turns HTTP header text into header objects.
/// Documented as taking "at least the headers"; this accepts text with or
/// without the trailing blank line, and with CRLF or bare LF line endings.
/// Anything after the first blank line (a body) is ignored.
/// </summary>
public static class Parser
{
    /// <summary>
    /// Parses a request line plus headers, e.g.
    /// <c>GET https://host/path HTTP/1.1\r\nAccept: */*</c>. The request
    /// target is kept exactly as given in
    /// <see cref="HTTPRequestHeaders.RequestPath"/>; when it's absolute-form,
    /// <see cref="HTTPRequestHeaders.UriScheme"/> comes from it. A missing
    /// version is taken as <c>HTTP/1.1</c>. Null if there's no parseable
    /// request line.
    /// </summary>
    public static HTTPRequestHeaders? ParseRequest(string sRequest)
    {
        if (string.IsNullOrWhiteSpace(sRequest))
        {
            return null;
        }

        var lines = HTTPHeaders.SplitLines(sRequest);
        if (lines.Count == 0)
        {
            return null;
        }

        var parts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var headers = new HTTPRequestHeaders(parts[1], lines.Skip(1).ToArray())
        {
            HTTPMethod = parts[0],
            HTTPVersion = parts.Length >= 3 ? parts[2] : "HTTP/1.1",
        };

        var schemeEnd = parts[1].IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            headers.UriScheme = parts[1][..schemeEnd].ToLowerInvariant();
        }

        return headers;
    }

    /// <summary>
    /// Parses a status line plus headers, e.g.
    /// <c>HTTP/1.1 200 OK\r\nContent-Type: text/plain</c>. Null if there's no
    /// parseable status line.
    /// </summary>
    public static HTTPResponseHeaders? ParseResponse(string sResponse)
    {
        if (string.IsNullOrWhiteSpace(sResponse))
        {
            return null;
        }

        var lines = HTTPHeaders.SplitLines(sResponse);
        if (lines.Count == 0)
        {
            return null;
        }

        var parts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
        {
            return null;
        }

        var description = parts.Length == 3 ? parts[2] : HTTPResponseHeaders.StandardReasonPhrase(statusCode);
        return new HTTPResponseHeaders(statusCode, description, lines.Skip(1).ToArray())
        {
            HTTPVersion = parts[0],
        };
    }
}
