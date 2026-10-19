namespace Violens.ProxyCore.Proxy.AutoConfig;

/// <summary>
/// Reads what <c>FindProxyForURL</c> returns: a list such as
/// "PROXY proxy1:8080; PROXY proxy2:8080; DIRECT", tried in order.
/// </summary>
public static class PacResult
{
    /// <summary>
    /// The routes to try, in order: an <see cref="UpstreamProxy"/>, or null
    /// for "connect directly". Entries VioLens can't use yet (SOCKS, an
    /// HTTPS proxy) are left out, and listed in <paramref name="unsupported"/>.
    /// An empty or unreadable answer means direct, as it does in browsers.
    /// </summary>
    public static IReadOnlyList<UpstreamProxy?> Parse(string? result, out IReadOnlyList<string> unsupported)
    {
        var routes = new List<UpstreamProxy?>();
        var skipped = new List<string>();
        unsupported = skipped;

        foreach (var entry in (result ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split((char[]?)null, 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var kind = parts[0].ToUpperInvariant();
            if (kind == "DIRECT")
            {
                if (!routes.Contains(null))
                {
                    routes.Add(null);
                }

                continue;
            }

            // "HTTP" is Chrome's and Firefox's synonym for "PROXY".
            if ((kind is "PROXY" or "HTTP") && parts.Length == 2 && UpstreamProxy.Parse(parts[1]) is { } proxy)
            {
                if (!routes.Contains(proxy))
                {
                    routes.Add(proxy);
                }

                continue;
            }

            skipped.Add(entry);
        }

        if (routes.Count == 0)
        {
            // Nothing usable: browsers treat an empty answer as DIRECT; for
            // a list of only SOCKS proxies, direct is the one thing left to try.
            routes.Add(null);
        }

        return routes;
    }
}
