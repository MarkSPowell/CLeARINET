using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Proxy.AutoConfig;

namespace Violens.ProxyCore.SystemProxy;

/// <summary>
/// What <see cref="SystemProxyController.DetectUpstream"/> found: the proxy
/// to forward through (null to connect directly) and a short description for
/// the status line. When the system uses automatic proxy configuration,
/// <see cref="AutoConfig"/> chooses per request instead, and
/// <see cref="Proxy"/> is null.
/// </summary>
public sealed record UpstreamProxyDetection(UpstreamProxy? Proxy, string Description)
{
    /// <summary>No system proxy: connect directly.</summary>
    public static UpstreamProxyDetection None { get; } = new(null, "connecting directly (no system proxy)");

    /// <summary>Automatic proxy configuration (a PAC script, found or configured). The caller owns it and disposes it.</summary>
    public ProxyAutoConfig? AutoConfig { get; init; }

    /// <summary>Where the settings came from, e.g. "Windows proxy settings".</summary>
    public string? Source { get; init; }

    internal static UpstreamProxyDetection Automatic(ProxyAutoConfig autoConfig, string source) =>
        new(null, $"{autoConfig.Description} ({source})") { AutoConfig = autoConfig, Source = source };
}
