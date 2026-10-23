namespace Violens.LegacyExtensionHost.Bridge;

/// <summary>
/// Which IAutoTamper hook to run, or <see cref="Capabilities"/>: the check
/// the app makes on Start to see whether a host is there and has anything
/// loaded.
/// </summary>
public enum BridgeMessageKind
{
    Capabilities,
    RequestBefore,
    RequestAfter,
    ResponseBefore,
    ResponseAfter,
}

/// <summary>
/// One call from the app to the legacy host; fields that don't apply to
/// <see cref="Kind"/> are left at their defaults (no response for the
/// request hooks).
/// </summary>
public sealed class BridgeRequestMessage
{
    public BridgeMessageKind Kind { get; set; }

    /// <summary><c>Fiddler.Session.id</c>; 0 for <see cref="BridgeMessageKind.Capabilities"/>.</summary>
    public int SessionOrdinal { get; set; }

    /// <summary>
    /// <c>Fiddler.Session.host</c>. Unused for <see cref="BridgeMessageKind.Capabilities"/>.
    /// </summary>
    public string? Hostname { get; set; }

    /// <summary>Present for every <see cref="BridgeMessageKind"/> except <see cref="BridgeMessageKind.Capabilities"/>.</summary>
    public WireRequest? Request { get; set; }

    /// <summary>Present only for <see cref="BridgeMessageKind.ResponseBefore"/>/<see cref="BridgeMessageKind.ResponseAfter"/>.</summary>
    public WireResponse? Response { get; set; }
}

/// <summary>
/// The legacy host's answer. Sent for every kind, including the After hooks
/// whose payload the app ignores, so every call works the same way.
/// </summary>
public sealed class BridgeResponseMessage
{
    /// <summary>
    /// False only when the host couldn't make the call at all (still loading,
    /// say). An extension that throws is logged and skipped; the answer is
    /// still Ok.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>Set when <see cref="Ok"/> is false, or when <see cref="BridgeMessageKind.Capabilities"/> is answered by a host with nothing loaded yet -- human-readable, logged, not parsed.</summary>
    public string? Error { get; set; }

    /// <summary>For <see cref="BridgeMessageKind.Capabilities"/>: how many loaded extensions implement <c>IAutoTamper</c>.</summary>
    public int AutoTamperCount { get; set; }

    /// <summary>The request after the extensions ran; the app uses it only for RequestBefore.</summary>
    public WireRequest? Request { get; set; }

    /// <summary>Same as <see cref="Request"/>, for <see cref="BridgeMessageKind.ResponseBefore"/>/<see cref="BridgeMessageKind.ResponseAfter"/>.</summary>
    public WireResponse? Response { get; set; }
}
