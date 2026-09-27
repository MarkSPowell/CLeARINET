using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>
/// Another HTTP proxy that CLeARINET forwards its own outgoing connections
/// through -- Fiddler Classic's "gateway". On a network that only reaches the
/// internet through a proxy, the system proxy setting points at that proxy
/// until CLeARINET registers itself in its place; without chaining to it,
/// everything CLeARINET forwards would try to connect directly and fail.
///
/// Only plain HTTP proxies are supported, reached with a <c>CONNECT</c>
/// tunnel. Not supported yet: automatic configuration scripts (PAC/WPAD),
/// SOCKS, and proxies that require authentication (a 407 response is
/// reported as an error saying so).
/// </summary>
/// <param name="Host">The proxy's host name or address.</param>
/// <param name="Port">The proxy's port.</param>
/// <param name="BypassList">
/// Hosts to connect to directly instead, in the Windows proxy-override
/// format: exact names, <c>*</c> wildcards (<c>*.corp.example</c>,
/// <c>10.*</c>), and <c>&lt;local&gt;</c> for names without a dot. Loopback
/// targets are always connected to directly.
/// </param>
public sealed record UpstreamProxy(string Host, int Port, IReadOnlyList<string> BypassList)
{
    private const int MaxResponseHeaderBytes = 64 * 1024;

    public UpstreamProxy(string host, int port)
        : this(host, port, [])
    {
    }

    public override string ToString() => $"{FormatHost(Host)}:{Port}";

    /// <summary>
    /// Parses "host:port" or "http://host:port" (a trailing "/" is ignored).
    /// Returns null for anything else, including an https:// or socks://
    /// proxy, which aren't supported.
    /// </summary>
    public static UpstreamProxy? Parse(string? text, IReadOnlyList<string>? bypassList = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().TrimEnd('/');
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["http://".Length..];
        }
        else if (value.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        string host;
        string portText;
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close < 0 || close + 1 >= value.Length || value[close + 1] != ':')
            {
                return null;
            }

            host = value[1..close];
            portText = value[(close + 2)..];
        }
        else
        {
            var colon = value.LastIndexOf(':');
            if (colon <= 0 || value.IndexOf(':') != colon)
            {
                return null;
            }

            host = value[..colon];
            portText = value[(colon + 1)..];
        }

        return int.TryParse(portText, out var port) && port is >= 1 and <= 65535 && host.Length > 0
            ? new UpstreamProxy(host, port, bypassList ?? Array.Empty<string>())
            : null;
    }

    /// <summary>
    /// Reads Windows' own proxy settings (the Internet Settings
    /// <c>ProxyServer</c> and <c>ProxyOverride</c> values). ProxyServer is
    /// either one "host:port" for every protocol or a per-protocol list such
    /// as "http=h:80;https=h:443"; the https entry is preferred (CLeARINET's
    /// outgoing connections are HTTPS), then http, then a bare entry. A list
    /// with only socks or ftp entries gives null.
    /// </summary>
    public static UpstreamProxy? FromWindowsSettings(string? proxyServer, string? proxyOverride)
    {
        if (string.IsNullOrWhiteSpace(proxyServer))
        {
            return null;
        }

        var bypass = (proxyOverride ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!proxyServer.Contains('='))
        {
            return Parse(proxyServer, bypass);
        }

        string? http = null;
        string? https = null;
        foreach (var entry in proxyServer.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = entry.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var scheme = entry[..equals].Trim();
            var address = entry[(equals + 1)..].Trim();
            if (string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                https = address;
            }
            else if (string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                http = address;
            }
        }

        return Parse(https ?? http, bypass);
    }

    /// <summary>True when <paramref name="host"/> should be connected to directly rather than through this proxy.</summary>
    public bool ShouldBypass(string host)
    {
        if (IsLoopback(host))
        {
            return true;
        }

        foreach (var entry in BypassList)
        {
            if (string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase))
            {
                if (!host.Contains('.') && !host.Contains(':'))
                {
                    return true;
                }

                continue;
            }

            var pattern = "^" + Regex.Escape(entry.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            if (Regex.IsMatch(host, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when this proxy is CLeARINET itself listening on <paramref name="ownPort"/> (a loopback address on that port).</summary>
    public bool IsSelf(int ownPort) => Port == ownPort && IsLoopback(Host);

    /// <summary>
    /// Connects to this proxy and asks it for a tunnel to
    /// <paramref name="targetHost"/>:<paramref name="targetPort"/>. Returns
    /// the connected client; its stream then carries the tunnelled
    /// connection (TLS to the target goes over it). Throws
    /// <see cref="IOException"/> when the proxy refuses.
    /// </summary>
    public async Task<TcpClient> ConnectTunnelAsync(string targetHost, int targetPort, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(Host, Port, cancellationToken);
            await EstablishTunnelAsync(client.GetStream(), targetHost, targetPort, ToString(), cancellationToken);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sends <c>CONNECT</c> for <paramref name="targetHost"/>:<paramref name="targetPort"/>
    /// on <paramref name="stream"/> (already connected to a proxy) and reads
    /// the proxy's answer. Returns once the tunnel is open; throws
    /// <see cref="IOException"/> otherwise. Used for upstream proxies and by
    /// the Composer, which sends through CLeARINET's own listener.
    /// </summary>
    public static async Task EstablishTunnelAsync(
        Stream stream, string targetHost, int targetPort, string proxyName, CancellationToken cancellationToken)
    {
        var authority = $"{FormatHost(targetHost)}:{targetPort}";
        var connect = $"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\nProxy-Connection: keep-alive\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(connect), cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var statusLine = await ReadResponseHeadAsync(stream, cancellationToken);
        var parts = statusLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var status))
        {
            throw new IOException($"Proxy {proxyName} sent an unreadable answer to CONNECT {authority}: \"{statusLine}\".");
        }

        if (status == 407)
        {
            throw new IOException(
                $"Proxy {proxyName} requires authentication (407), which CLeARINET doesn't support yet. " +
                "Set an upstream proxy that doesn't need a sign-in, or \"none\" to connect directly.");
        }

        if (status is < 200 or > 299)
        {
            throw new IOException($"Proxy {proxyName} refused CONNECT {authority}: \"{statusLine}\".");
        }
    }

    /// <summary>Reads a response's status line and headers, up to the blank line, and returns the status line.</summary>
    private static async Task<string> ReadResponseHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(), cancellationToken);
            if (read == 0)
            {
                throw new IOException("The proxy closed the connection before answering CONNECT.");
            }

            bytes.Add(one[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
            {
                break;
            }

            if (count >= 2 && bytes[count - 2] == '\n' && bytes[count - 1] == '\n')
            {
                break;
            }

            if (count > MaxResponseHeaderBytes)
            {
                throw new IOException("The proxy's answer to CONNECT was too long.");
            }
        }

        var text = Encoding.ASCII.GetString(bytes.ToArray());
        var endOfLine = text.IndexOf('\n');
        return (endOfLine < 0 ? text : text[..endOfLine]).TrimEnd('\r');
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

    private static string FormatHost(string host) =>
        host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
}
