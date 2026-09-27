namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Shows a message's cookies, one per row, parsed per RFC 6265 (and the
/// cookie-prefix rules in its successor draft, RFC 6265bis).
///
/// <list type="bullet">
/// <item><b>Request side</b>: every cookie in the <c>Cookie</c> header(s),
/// as name and value.</item>
/// <item><b>Response side</b>: one row per <c>Set-Cookie</c> header: the
/// cookie's name, then its value followed by its attributes (Path, Domain,
/// Expires, Max-Age, Secure, HttpOnly, SameSite, ...). Settings that
/// browsers reject are called out after the attributes.</item>
/// </list>
///
/// Appears only on a side that has cookies. CLeARINET's own feature, built
/// from the RFCs, not from any extension's code (see "Port or build in?" in
/// the Extension Test Targets doc). P3P, which the old Fiddler cookie
/// sample also showed, is obsolete; its header still shows on the Headers
/// tab.
/// </summary>
public sealed class CookiesInspector : IInspector
{
    public string Id => "clearinet.cookies";

    public string DisplayName => "Cookies";

    public int SortOrder => 25;

    public bool CanInspect(InspectorContext context) => context.Headers.Any(h => IsCookieHeader(context.Side, h.Name));

    public InspectorContent Inspect(InspectorContext context)
    {
        var rows = new List<HeaderRow>();
        foreach (var (name, value) in context.Headers)
        {
            if (!IsCookieHeader(context.Side, name))
            {
                continue;
            }

            if (context.Side == InspectorSide.Request)
            {
                rows.AddRange(ParseCookieHeader(value));
            }
            else
            {
                rows.Add(ParseSetCookie(value));
            }
        }

        return new KeyValueContent(rows);
    }

    private static bool IsCookieHeader(InspectorSide side, string headerName) =>
        string.Equals(headerName, side == InspectorSide.Request ? "Cookie" : "Set-Cookie", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>Cookie: a=1; b=2</c> → one row per pair. A piece with no <c>=</c> is a value with an empty name.</summary>
    internal static IEnumerable<HeaderRow> ParseCookieHeader(string header)
    {
        foreach (var piece in header.Split(';'))
        {
            var pair = piece.Trim();
            if (pair.Length == 0)
            {
                continue;
            }

            var equals = pair.IndexOf('=');
            yield return equals < 0
                ? new HeaderRow("(no name)", pair)
                : new HeaderRow(pair[..equals].Trim(), pair[(equals + 1)..].Trim());
        }
    }

    /// <summary>One <c>Set-Cookie</c> value → name, and value with attributes and any warnings.</summary>
    internal static HeaderRow ParseSetCookie(string header)
    {
        var parts = header.Split(';');
        var pair = parts[0].Trim();
        var equals = pair.IndexOf('=');
        var name = equals < 0 ? string.Empty : pair[..equals].Trim();
        var value = equals < 0 ? pair : pair[(equals + 1)..].Trim();

        var attributes = new List<string>();
        var attributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? sameSite = null;
        string? path = null;
        foreach (var raw in parts.Skip(1))
        {
            var attribute = raw.Trim();
            if (attribute.Length == 0)
            {
                continue;
            }

            attributes.Add(attribute);
            var attributeEquals = attribute.IndexOf('=');
            var attributeName = (attributeEquals < 0 ? attribute : attribute[..attributeEquals]).Trim();
            var attributeValue = attributeEquals < 0 ? string.Empty : attribute[(attributeEquals + 1)..].Trim();
            attributeNames.Add(attributeName);

            if (attributeName.Equals("SameSite", StringComparison.OrdinalIgnoreCase))
            {
                sameSite = attributeValue;
            }
            else if (attributeName.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                path = attributeValue;
            }
        }

        var secure = attributeNames.Contains("Secure");
        var warnings = new List<string>();
        if (equals < 0)
        {
            warnings.Add("no '=' in the name-value pair; RFC 6265 says to ignore this cookie, newer browsers treat it as a value with no name");
        }

        if (string.Equals(sameSite, "None", StringComparison.OrdinalIgnoreCase) && !secure)
        {
            warnings.Add("SameSite=None without Secure; browsers reject this cookie");
        }

        if (name.StartsWith("__Secure-", StringComparison.Ordinal) && !secure)
        {
            warnings.Add("the __Secure- prefix requires Secure; browsers reject this cookie");
        }

        if (name.StartsWith("__Host-", StringComparison.Ordinal) && (!secure || path != "/" || attributeNames.Contains("Domain")))
        {
            warnings.Add("the __Host- prefix requires Secure, Path=/ and no Domain; browsers reject this cookie");
        }

        var text = value;
        if (attributes.Count > 0)
        {
            text += "   [" + string.Join("; ", attributes) + "]";
        }

        foreach (var warning in warnings)
        {
            text += "   Warning: " + warning + ".";
        }

        return new HeaderRow(name.Length == 0 ? "(no name)" : name, text);
    }
}
