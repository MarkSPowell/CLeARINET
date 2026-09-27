using System.Collections;
using System.Text;

namespace Clearinet.CompatShim;

/// <summary>One header line: a name and a value. Fiddler exposes these as public fields.</summary>
public class HTTPHeaderItem : ICloneable
{
    public string Name;
    public string Value;

    public HTTPHeaderItem(string sName, string sValue)
    {
        Name = sName ?? string.Empty;
        Value = sValue ?? string.Empty;
    }

    public object Clone() => new HTTPHeaderItem(Name, Value);

    public override string ToString() => $"{Name}: {Value}";
}

/// <summary>
/// An ordered, case-insensitive list of headers, allowing repeats -- the
/// shape Fiddler documents for <c>HTTPHeaders</c>. Member semantics follow
/// Telerik's published FiddlerCore API reference; in particular, the
/// string indexer returns <see langword="null"/> (not an empty string) for
/// a header that isn't present, and <see cref="Remove(string)"/> /
/// <see cref="RenameHeaderItems"/> act on every header of that name.
/// </summary>
public abstract class HTTPHeaders : IEnumerable<HTTPHeaderItem>
{
    protected readonly List<HTTPHeaderItem> storage = new();

    /// <summary>The HTTP version, e.g. <c>HTTP/1.1</c>.</summary>
    public string HTTPVersion { get; set; } = "HTTP/1.1";

    /// <summary>
    /// Get: the first header of this name's value, or <see langword="null"/>.
    /// Set: updates the first header of this name (adding one if there's
    /// none). Setting <see langword="null"/> removes every header of that
    /// name.
    /// </summary>
    public string? this[string HeaderName]
    {
        get => FindFirst(HeaderName)?.Value;
        set
        {
            if (value is null)
            {
                Remove(HeaderName);
                return;
            }

            var existing = FindFirst(HeaderName);
            if (existing is null)
            {
                Add(HeaderName, value);
            }
            else
            {
                existing.Value = value;
            }
        }
    }

    /// <summary>The header at this position.</summary>
    public HTTPHeaderItem this[int iHeaderNumber]
    {
        get => storage[iHeaderNumber];
        set => storage[iHeaderNumber] = value;
    }

    /// <summary>Adds a header, even if one of the same name already exists. Returns the new item.</summary>
    public HTTPHeaderItem Add(string sHeaderName, string sValue)
    {
        var item = new HTTPHeaderItem(sHeaderName, sValue);
        storage.Add(item);
        return item;
    }

    /// <summary>The number of headers.</summary>
    public int Count() => storage.Count;

    public bool Exists(string sHeaderName) => FindFirst(sHeaderName) is not null;

    public bool ExistsAny(IEnumerable<string> sHeaderNames) => sHeaderNames.Any(Exists);

    /// <summary>True if a header of this name exists and its value contains <paramref name="sHeaderValue"/> (case-insensitive).</summary>
    public bool ExistsAndContains(string sHeaderName, string sHeaderValue) =>
        storage.Any(h => NameIs(h, sHeaderName) && h.Value.Contains(sHeaderValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if a header of this name exists with exactly this value (case-insensitive).</summary>
    public bool ExistsAndEquals(string sHeaderName, string sHeaderValue) =>
        storage.Any(h => NameIs(h, sHeaderName) && string.Equals(h.Value.Trim(), sHeaderValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every value of this header, joined with <c>", "</c>.</summary>
    public string AllValues(string sHeaderName) =>
        string.Join(", ", storage.Where(h => NameIs(h, sHeaderName)).Select(h => h.Value));

    public List<HTTPHeaderItem> FindAll(string sHeaderName) => storage.Where(h => NameIs(h, sHeaderName)).ToList();

    /// <summary>Removes every header with this name.</summary>
    public void Remove(string sHeaderName) => storage.RemoveAll(h => NameIs(h, sHeaderName));

    public void Remove(HTTPHeaderItem oRemove) => storage.Remove(oRemove);

    public void RemoveRange(string[] arrToRemove)
    {
        foreach (var name in arrToRemove)
        {
            Remove(name);
        }
    }

    public void RemoveAll() => storage.Clear();

    /// <summary>Renames every header of the old name. True if any were renamed.</summary>
    public bool RenameHeaderItems(string sOldHeaderName, string sNewHeaderName)
    {
        var renamed = false;
        foreach (var item in storage.Where(h => NameIs(h, sOldHeaderName)))
        {
            item.Name = sNewHeaderName;
            renamed = true;
        }

        return renamed;
    }

    /// <summary>
    /// The value of a <c>name=value</c> token inside a header, e.g.
    /// <c>GetTokenValue("Content-Type", "charset")</c> → <c>utf-8</c>.
    /// Null if the header or token isn't there.
    /// </summary>
    public string? GetTokenValue(string sHeaderName, string sTokenName)
    {
        var header = this[sHeaderName];
        if (header is null)
        {
            return null;
        }

        foreach (var part in header.Split(';', ','))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            if (string.Equals(part[..equals].Trim(), sTokenName, StringComparison.OrdinalIgnoreCase))
            {
                return part[(equals + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }

    public IEnumerator<HTTPHeaderItem> GetEnumerator() => storage.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The header lines only, separated by CRLF, no trailing CRLF.</summary>
    public override string ToString() => string.Join("\r\n", storage.Select(h => h.ToString()));

    /// <summary>
    /// Replaces this object's contents with what's parsed from
    /// <paramref name="sHeaders"/>: a start line (request or status line,
    /// handled by the subclass) followed by header lines. False, leaving
    /// this object unchanged, if it can't be parsed.
    /// </summary>
    public abstract bool AssignFromString(string sHeaders);

    /// <summary>
    /// Splits header text into lines (CRLF or bare LF), stopping at the
    /// first blank line after the content starts.
    /// </summary>
    protected internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (lines.Count > 0)
                {
                    break;
                }

                continue;
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// Parses <c>Name: Value</c> lines. A line starting with whitespace
    /// continues the previous header's value (obsolete line folding). A
    /// name may itself start with <c>:</c> (HTTP/2 pseudo-headers), in which
    /// case the separator is the next colon after it. A line with no colon
    /// becomes a header with an empty value rather than being dropped.
    /// </summary>
    protected internal static List<HTTPHeaderItem> ParseHeaderLines(IEnumerable<string> lines)
    {
        var items = new List<HTTPHeaderItem>();
        foreach (var line in lines)
        {
            if ((line[0] == ' ' || line[0] == '\t') && items.Count > 0)
            {
                items[^1].Value += " " + line.Trim();
                continue;
            }

            var separator = line.IndexOf(':', line[0] == ':' ? 1 : 0);
            items.Add(separator < 0
                ? new HTTPHeaderItem(line.Trim(), string.Empty)
                : new HTTPHeaderItem(line[..separator].Trim(), line[(separator + 1)..].Trim()));
        }

        return items;
    }

    protected void ReplaceHeaders(IEnumerable<HTTPHeaderItem> items)
    {
        storage.Clear();
        storage.AddRange(items);
    }

    protected void CopyHeadersTo(HTTPHeaders target)
    {
        target.HTTPVersion = HTTPVersion;
        target.storage.Clear();
        target.storage.AddRange(storage.Select(h => (HTTPHeaderItem)h.Clone()));
    }

    private HTTPHeaderItem? FindFirst(string name) => storage.FirstOrDefault(h => NameIs(h, name));

    private static bool NameIs(HTTPHeaderItem item, string name) =>
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Request headers plus the request line. <see cref="RequestPath"/> is kept
/// exactly as given, which may be origin-form (<c>/path?q</c>) or, as in a
/// proxy request or an imported capture, absolute-form
/// (<c>https://host/path?q</c>) -- <see cref="Session.fullUrl"/> handles
/// both.
/// </summary>
public class HTTPRequestHeaders : HTTPHeaders, ICloneable
{
    /// <summary>The HTTP method. A public field, as in Fiddler.</summary>
    public string HTTPMethod = "GET";

    public HTTPRequestHeaders()
    {
    }

    /// <summary>
    /// Builds request headers from a path and <c>"Name: Value"</c> lines --
    /// Fiddler's documented convenience constructor for script- or
    /// importer-generated sessions. The method is <c>GET</c>.
    /// </summary>
    public HTTPRequestHeaders(string sPath, string[] sHeaders)
    {
        RequestPath = sPath ?? string.Empty;
        ReplaceHeaders(ParseHeaderLines((sHeaders ?? []).Where(h => !string.IsNullOrEmpty(h))));
    }

    /// <summary>The request target, e.g. <c>/path.htm</c>, exactly as given.</summary>
    public string RequestPath { get; set; } = string.Empty;

    /// <summary>The URI scheme, usually <c>http</c> or <c>https</c>. Taken from an absolute-form request target when there is one.</summary>
    public string UriScheme { get; set; } = "http";

    /// <summary>For FTP URLs, <c>user:pass@</c>; otherwise null.</summary>
    public string? UriUserInfo { get; set; }

    public object Clone()
    {
        var clone = new HTTPRequestHeaders
        {
            HTTPMethod = HTTPMethod,
            RequestPath = RequestPath,
            UriScheme = UriScheme,
            UriUserInfo = UriUserInfo,
        };
        CopyHeadersTo(clone);
        return clone;
    }

    /// <summary>The request line and headers, no trailing blank line.</summary>
    public override string ToString() => ToString(true, false);

    public string ToString(bool prependVerbLine, bool appendEmptyLine)
    {
        var sb = new StringBuilder();
        if (prependVerbLine)
        {
            sb.Append(HTTPMethod).Append(' ').Append(RequestPath).Append(' ').Append(HTTPVersion).Append("\r\n");
        }

        foreach (var header in this)
        {
            sb.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        if (appendEmptyLine)
        {
            sb.Append("\r\n");
        }
        else if (sb.Length >= 2)
        {
            sb.Length -= 2;
        }

        return sb.ToString();
    }

    public override bool AssignFromString(string sHeaders)
    {
        var parsed = Parser.ParseRequest(sHeaders);
        if (parsed is null)
        {
            return false;
        }

        HTTPMethod = parsed.HTTPMethod;
        RequestPath = parsed.RequestPath;
        UriScheme = parsed.UriScheme;
        UriUserInfo = parsed.UriUserInfo;
        parsed.CopyHeadersTo(this);
        return true;
    }
}

/// <summary>Response headers plus the status line.</summary>
public class HTTPResponseHeaders : HTTPHeaders, ICloneable
{
    /// <summary>The status code. A public field, as in Fiddler, which recommends <see cref="SetStatus"/> for changing it.</summary>
    public int HTTPResponseCode;

    /// <summary>Code and description together, e.g. <c>200 OK</c>. A public field, as in Fiddler; <see cref="SetStatus"/> keeps it in step with <see cref="HTTPResponseCode"/>.</summary>
    public string HTTPResponseStatus = string.Empty;

    public HTTPResponseHeaders()
    {
    }

    /// <summary>Status code plus <c>"Name: Value"</c> lines; the description is the standard one for that code.</summary>
    public HTTPResponseHeaders(int iStatus, string[] sHeaders)
        : this(iStatus, StandardReasonPhrase(iStatus), sHeaders)
    {
    }

    /// <summary>Status code, description and <c>"Name: Value"</c> lines -- Fiddler's documented convenience constructor.</summary>
    public HTTPResponseHeaders(int iStatusCode, string sStatusText, string[] sHeaders)
    {
        SetStatus(iStatusCode, sStatusText);
        ReplaceHeaders(ParseHeaderLines((sHeaders ?? []).Where(h => !string.IsNullOrEmpty(h))));
    }

    /// <summary>The text after the code, e.g. <c>OK</c>.</summary>
    public string StatusDescription
    {
        get
        {
            var space = HTTPResponseStatus.IndexOf(' ');
            return space < 0 ? string.Empty : HTTPResponseStatus[(space + 1)..];
        }
        set => HTTPResponseStatus = $"{HTTPResponseCode} {value}";
    }

    /// <summary>Sets the code and description together.</summary>
    public void SetStatus(int iCode, string sDescription)
    {
        HTTPResponseCode = iCode;
        HTTPResponseStatus = $"{iCode} {sDescription}";
    }

    public object Clone()
    {
        var clone = new HTTPResponseHeaders
        {
            HTTPResponseCode = HTTPResponseCode,
            HTTPResponseStatus = HTTPResponseStatus,
        };
        CopyHeadersTo(clone);
        return clone;
    }

    /// <summary>The status line and headers, no trailing blank line.</summary>
    public override string ToString() => ToString(true, false);

    public string ToString(bool prependStatusLine, bool appendEmptyLine)
    {
        var sb = new StringBuilder();
        if (prependStatusLine)
        {
            sb.Append(HTTPVersion).Append(' ').Append(HTTPResponseStatus).Append("\r\n");
        }

        foreach (var header in this)
        {
            sb.Append(header.Name).Append(": ").Append(header.Value).Append("\r\n");
        }

        if (appendEmptyLine)
        {
            sb.Append("\r\n");
        }
        else if (sb.Length >= 2)
        {
            sb.Length -= 2;
        }

        return sb.ToString();
    }

    public override bool AssignFromString(string sHeaders)
    {
        var parsed = Parser.ParseResponse(sHeaders);
        if (parsed is null)
        {
            return false;
        }

        HTTPResponseCode = parsed.HTTPResponseCode;
        HTTPResponseStatus = parsed.HTTPResponseStatus;
        parsed.CopyHeadersTo(this);
        return true;
    }

    /// <summary>The usual reason phrase for a status code, or an empty string for an unusual one.</summary>
    public static string StandardReasonPhrase(int statusCode) => statusCode switch
    {
        100 => "Continue",
        101 => "Switching Protocols",
        103 => "Early Hints",
        200 => "OK",
        201 => "Created",
        202 => "Accepted",
        203 => "Non-Authoritative Information",
        204 => "No Content",
        206 => "Partial Content",
        301 => "Moved Permanently",
        302 => "Found",
        303 => "See Other",
        304 => "Not Modified",
        307 => "Temporary Redirect",
        308 => "Permanent Redirect",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        407 => "Proxy Authentication Required",
        408 => "Request Timeout",
        409 => "Conflict",
        410 => "Gone",
        412 => "Precondition Failed",
        413 => "Content Too Large",
        415 => "Unsupported Media Type",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        501 => "Not Implemented",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        504 => "Gateway Timeout",
        _ => string.Empty,
    };
}
