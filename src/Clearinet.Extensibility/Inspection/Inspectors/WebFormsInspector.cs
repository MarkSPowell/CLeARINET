using System.Text;
using Clearinet.ProxyCore.Http;

namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's "WebForms" inspector: a request's query string and
/// form fields, one row per name/value pair, decoded.
///
/// Reads the query string from the request target, and the body when it's
/// <c>application/x-www-form-urlencoded</c> or <c>multipart/form-data</c>
/// (a file part shows its file name, type and size rather than its bytes).
/// When there's both a query string and a form body, a marker row names
/// where each group of rows came from. Request side only.
/// </summary>
public sealed class WebFormsInspector : IInspector
{
    internal const string QueryMarker = "[Query string]";
    internal const string BodyMarker = "[Body]";

    public string Id => "clearinet.webforms";

    public string DisplayName => "WebForms";

    public int SortOrder => 14;

    public bool CanInspect(InspectorContext context) =>
        context.Side == InspectorSide.Request &&
        (QueryString(context.Session.Request.Target).Length > 0 || BodyKind(context) is not BodyFormat.None);

    public InspectorContent Inspect(InspectorContext context)
    {
        var queryRows = ParseUrlEncoded(QueryString(context.Session.Request.Target));

        var bodyRows = new List<HeaderRow>();
        var kind = BodyKind(context);
        if (kind is not BodyFormat.None && context.Body.Length > 0)
        {
            if (!ContentDecoder.TryDecode(context.Body, context.FindHeader("Content-Encoding"), out var decoded, out var error))
            {
                return new ErrorContent(error!);
            }

            var contentType = context.FindHeader("Content-Type");
            bodyRows = kind == BodyFormat.UrlEncoded
                ? ParseUrlEncoded(ContentDecoder.DecodeText(decoded, contentType))
                : ParseMultipart(decoded, contentType);
        }

        if (queryRows.Count == 0 || bodyRows.Count == 0)
        {
            return new KeyValueContent(queryRows.Count == 0 ? bodyRows : queryRows);
        }

        var rows = new List<HeaderRow> { new(QueryMarker, string.Empty) };
        rows.AddRange(queryRows);
        rows.Add(new HeaderRow(BodyMarker, string.Empty));
        rows.AddRange(bodyRows);
        return new KeyValueContent(rows);
    }

    private enum BodyFormat
    {
        None,
        UrlEncoded,
        Multipart,
    }

    private static BodyFormat BodyKind(InspectorContext context)
    {
        if (context.Body.Length == 0)
        {
            return BodyFormat.None;
        }

        return ContentDecoder.MediaType(context.FindHeader("Content-Type")) switch
        {
            "application/x-www-form-urlencoded" => BodyFormat.UrlEncoded,
            "multipart/form-data" => BodyFormat.Multipart,
            _ => BodyFormat.None,
        };
    }

    /// <summary>The query string (without "?" or any "#fragment") of an origin-form or absolute request target.</summary>
    internal static string QueryString(string target)
    {
        var question = target.IndexOf('?');
        if (question < 0)
        {
            return string.Empty;
        }

        var query = target[(question + 1)..];
        var hash = query.IndexOf('#');
        return hash < 0 ? query : query[..hash];
    }

    internal static List<HeaderRow> ParseUrlEncoded(string text)
    {
        var rows = new List<HeaderRow>();
        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
            rows.Add(new HeaderRow(Unescape(name), Unescape(value)));
        }

        return rows;
    }

    private static string Unescape(string text)
    {
        var withSpaces = text.Replace('+', ' ');
        try
        {
            return Uri.UnescapeDataString(withSpaces);
        }
        catch (UriFormatException)
        {
            return withSpaces;
        }
    }

    /// <summary>
    /// One row per part of a <c>multipart/form-data</c> body (RFC 7578): its
    /// field name, and its text or, for a file, "(file: name, type, N bytes)".
    /// Tolerant: a malformed part is skipped, not an error.
    /// </summary>
    internal static List<HeaderRow> ParseMultipart(byte[] body, string? contentType)
    {
        var rows = new List<HeaderRow>();
        var boundary = Parameter(contentType, "boundary");
        if (string.IsNullOrEmpty(boundary))
        {
            rows.Add(new HeaderRow("(multipart body)", "No boundary in Content-Type; see the Raw tab."));
            return rows;
        }

        // Latin-1 maps every byte to one char and back, so positions found in
        // the string are byte offsets into the body.
        var text = Encoding.Latin1.GetString(body);
        var delimiter = "--" + boundary;
        var position = text.IndexOf(delimiter, StringComparison.Ordinal);
        while (position >= 0)
        {
            var partStart = position + delimiter.Length;
            if (text.Length >= partStart + 2 && text[partStart] == '-' && text[partStart + 1] == '-')
            {
                break;
            }

            var next = text.IndexOf(delimiter, partStart, StringComparison.Ordinal);
            if (next < 0)
            {
                break;
            }

            var part = text[partStart..next];
            if (part.StartsWith("\r\n", StringComparison.Ordinal))
            {
                part = part[2..];
            }

            if (part.EndsWith("\r\n", StringComparison.Ordinal))
            {
                part = part[..^2];
            }

            var headerEnd = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                rows.Add(ParsePart(part[..headerEnd], part[(headerEnd + 4)..]));
            }

            position = next;
        }

        return rows;
    }

    private static HeaderRow ParsePart(string headerBlock, string content)
    {
        string? disposition = null;
        string? partType = null;
        foreach (var line in headerBlock.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (string.Equals(name, "Content-Disposition", StringComparison.OrdinalIgnoreCase))
            {
                disposition = value;
            }
            else if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                partType = value;
            }
        }

        var fieldName = Utf8(Parameter(disposition, "name") ?? "(no name)");
        var fileName = Parameter(disposition, "filename");
        if (fileName is not null)
        {
            var type = string.IsNullOrEmpty(partType) ? "unknown type" : partType;
            return new HeaderRow(fieldName, $"(file: {Utf8(fileName)}, {type}, {content.Length:N0} bytes)");
        }

        return new HeaderRow(fieldName, Utf8(content));
    }

    /// <summary>Re-reads a Latin-1 string's bytes as UTF-8 (how browsers send form text).</summary>
    private static string Utf8(string latin1) => Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(latin1));

    /// <summary>A parameter such as <c>boundary=...</c> or <c>name="..."</c> from a header value, unquoted.</summary>
    internal static string? Parameter(string? headerValue, string name)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return null;
        }

        foreach (var segment in headerValue.Split(';').Skip(1))
        {
            var equals = segment.IndexOf('=');
            if (equals < 0 || !string.Equals(segment[..equals].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = segment[(equals + 1)..].Trim();
            return value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
        }

        return null;
    }
}
