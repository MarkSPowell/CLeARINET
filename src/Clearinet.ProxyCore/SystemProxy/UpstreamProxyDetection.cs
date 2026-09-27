using Clearinet.ProxyCore.Proxy;

namespace Clearinet.ProxyCore.SystemProxy;

/// <summary>
/// What <see cref="SystemProxyController.DetectUpstream"/> found: the proxy
/// to forward through (null to connect directly) and a short description for
/// the status line.
/// </summary>
public sealed record UpstreamProxyDetection(UpstreamProxy? Proxy, string Description)
{
    /// <summary>No system proxy: connect directly.</summary>
    public static UpstreamProxyDetection None { get; } = new(null, "connecting directly (no system proxy)");
}
