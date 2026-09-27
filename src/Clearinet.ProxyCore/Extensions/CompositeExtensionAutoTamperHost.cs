using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Extensions;

/// <summary>
/// Runs several <see cref="IExtensionAutoTamperHost"/> implementations in
/// sequence as if they were one -- lets <c>MainWindowViewModel</c> (the
/// composition root) hand <see cref="Proxy.InterceptingProxyListener"/> a
/// single <see cref="IExtensionAutoTamperHost"/> that actually covers both
/// the in-process compiled-extension host
/// (<c>Clearinet.Compatibility.Extensions.LoadedExtensionSet</c>) and the
/// out-of-process legacy session bridge
/// (<c>Clearinet.Compatibility.Extensions.LegacyExtensionHostBridgeClient</c>
/// -- see the design doc's "Session bridge" section) without
/// <see cref="Proxy.InterceptingProxyListener"/> itself having to know more
/// than one extension source exists.
///
/// Threads a request/response through every wrapped host in order, each
/// seeing the previous one's edits -- the same "one shared, progressively
/// edited instance" shape <c>LoadedExtensionSet</c> already uses across
/// several loaded <c>IAutoTamper</c> instances, one level up: here each
/// wrapped host may itself run several extensions internally.
/// </summary>
public sealed class CompositeExtensionAutoTamperHost : IExtensionAutoTamperHost
{
    private readonly IReadOnlyList<IExtensionAutoTamperHost> _hosts;

    public CompositeExtensionAutoTamperHost(IReadOnlyList<IExtensionAutoTamperHost> hosts)
    {
        _hosts = hosts;
    }

    /// <inheritdoc/>
    public bool HasAnyRequestBeforeHandlers => _hosts.Any(h => h.HasAnyRequestBeforeHandlers);

    /// <inheritdoc/>
    public bool HasAnyResponseBeforeHandlers => _hosts.Any(h => h.HasAnyResponseBeforeHandlers);

    /// <inheritdoc/>
    public CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        var current = request;
        foreach (var host in _hosts)
        {
            current = host.RunRequestBefore(sessionOrdinal, hostname, current);
        }

        return current;
    }

    /// <inheritdoc/>
    public void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        foreach (var host in _hosts)
        {
            host.RunRequestAfter(sessionOrdinal, hostname, request);
        }
    }

    /// <inheritdoc/>
    public CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        var current = response;
        foreach (var host in _hosts)
        {
            current = host.RunResponseBefore(sessionOrdinal, hostname, request, current);
        }

        return current;
    }

    /// <inheritdoc/>
    public void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        foreach (var host in _hosts)
        {
            host.RunResponseAfter(sessionOrdinal, hostname, request, response);
        }
    }
}
