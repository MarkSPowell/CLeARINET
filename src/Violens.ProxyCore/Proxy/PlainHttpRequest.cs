using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Proxy;

/// <summary>
/// The rules for plain (unencrypted) <c>http://</c> requests a client sends
/// through VioLens as its proxy. Such a request names the whole URL in its
/// request line (<c>GET http://example.com/a HTTP/1.1</c>, the "absolute
/// form"), unlike a request inside an HTTPS tunnel, which names just the path.
///
/// VioLens stores and processes plain HTTP requests in the same shape as
/// HTTPS ones -- a path-only target plus a Host header, with the scheme kept
/// on the session -- so AutoResponder, breakpoints, FiddlerScript and
/// extensions see the same thing either way. <see cref="ForUpstream"/> turns
/// that back into what goes on the wire.
/// </summary>
internal static class PlainHttpRequest
{
    public const int DefaultPort = 80;

    /// <summary>
    /// Rewrites an absolute-form <c>http://</c> request to a path-only target,
    /// with the Host header set from the URL (RFC 9112 section 3.2.2: the
    /// URL's authority replaces any Host header the client sent), and returns
    /// where it goes. A path-only target is accepted when there's a Host
    /// header. Returns false for anything else, such as an <c>https://</c>
    /// or <c>ftp://</c> URL sent without a CONNECT tunnel.
    /// </summary>
    public static bool TryNormalize(CapturedRequest request, out CapturedRequest normalized, out string host, out int port)
    {
        normalized = request;
        host = string.Empty;
        port = DefaultPort;

        var target = request.Target;
        if (target.StartsWith('/'))
        {
            return TryGetRoute(request.Headers, out host, out port);
        }

        const string scheme = "http://";
        if (!target.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = target[scheme.Length..];
        var authorityEnd = rest.IndexOfAny(['/', '?', '#']);
        var authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
        var pathAndQuery = authorityEnd < 0 ? "/" : rest[authorityEnd..];
        var hash = pathAndQuery.IndexOf('#');
        if (hash >= 0)
        {
            pathAndQuery = pathAndQuery[..hash];
        }

        if (pathAndQuery.Length == 0 || pathAndQuery[0] == '?')
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            authority = authority[(at + 1)..]; // user:password@ never goes in a Host header
        }

        if (!TryParseAuthority(authority, out host, out port))
        {
            return false;
        }

        normalized = request with { Target = pathAndQuery, Headers = WithHost(request.Headers, authority) };
        return true;
    }

    /// <summary>Where a request goes, from its Host header ("host" or "host:port"; port 80 if none).</summary>
    public static bool TryGetRoute(IReadOnlyList<(string Name, string Value)> headers, out string host, out int port)
    {
        var hostHeader = ContentDecoder.FindHeader(headers, "Host");
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            host = string.Empty;
            port = DefaultPort;
            return false;
        }

        return TryParseAuthority(hostHeader.Trim(), out host, out port);
    }

    /// <summary>
    /// The request as sent onward: the proxy-only headers a client sends to
    /// VioLens (<c>Proxy-Connection</c>, <c>Proxy-Authorization</c>)
    /// removed, and, when sending through another proxy
    /// (<paramref name="absoluteForm"/>), the full URL put back in the
    /// request line.
    /// </summary>
    public static CapturedRequest ForUpstream(CapturedRequest request, bool absoluteForm)
    {
        var headers = request.Headers
            .Where(h => !string.Equals(h.Name, "Proxy-Connection", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(h.Name, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var target = request.Target;
        if (absoluteForm && target.StartsWith('/'))
        {
            var authority = ContentDecoder.FindHeader(request.Headers, "Host") ?? string.Empty;
            target = $"http://{authority}{target}";
        }

        return request with { Target = target, Headers = headers };
    }

    /// <summary>Parses "host", "host:port", "[v6]" or "[v6]:port".</summary>
    internal static bool TryParseAuthority(string authority, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        if (authority.Length == 0)
        {
            return false;
        }

        string portText;
        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            host = authority[1..close];
            portText = close + 1 < authority.Length && authority[close + 1] == ':' ? authority[(close + 2)..] : string.Empty;
        }
        else
        {
            var colon = authority.LastIndexOf(':');
            if (colon >= 0 && authority.IndexOf(':') != colon)
            {
                return false; // a bare IPv6 address needs brackets
            }

            host = colon < 0 ? authority : authority[..colon];
            portText = colon < 0 ? string.Empty : authority[(colon + 1)..];
        }

        if (portText.Length > 0 && !(int.TryParse(portText, out port) && port is >= 1 and <= 65535))
        {
            return false;
        }

        return host.Length > 0;
    }

    private static List<(string Name, string Value)> WithHost(IReadOnlyList<(string Name, string Value)> headers, string authority)
    {
        var result = new List<(string Name, string Value)>(headers.Count + 1);
        var placed = false;
        foreach (var (name, value) in headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                if (!placed)
                {
                    result.Add((name, authority));
                    placed = true;
                }

                continue;
            }

            result.Add((name, value));
        }

        if (!placed)
        {
            result.Insert(0, ("Host", authority));
        }

        return result;
    }
}
