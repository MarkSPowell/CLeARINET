namespace Clearinet.LegacyExtensionHost.Bridge;

/// <summary>
/// Which of <c>Fiddler.IAutoTamper</c>'s four hooks (see the CompatShim
/// project's own <c>IAutoTamper.cs</c>) a <see cref="BridgeRequestMessage"/>
/// is asking the legacy host to run, plus one extra, non-hook message:
/// <see cref="Capabilities"/> is how the main app's bridge client checks,
/// once per proxy <c>Start()</c>, whether a legacy host process is even
/// reachable and has anything loaded worth calling -- see
/// <c>LegacyExtensionHostBridgeClient</c>'s own remarks for why that
/// probe-once-then-cache shape matters.
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
/// One call across the session bridge, main app to legacy host. Fields not
/// relevant to <see cref="Kind"/> are left at their default (e.g.
/// <see cref="Response"/> is always null for <see cref="BridgeMessageKind.RequestBefore"/>/
/// <see cref="BridgeMessageKind.RequestAfter"/>, since real Fiddler's own
/// <c>AutoTamperRequestBefore</c>/<c>AutoTamperRequestAfter</c> fire before
/// any response exists -- see <c>Clearinet.Compatibility.FiddlerScript.Exchange.ForRequest</c>
/// for the same "no response yet" shape on the in-process side of this same
/// distinction) -- one envelope type for all five kinds is simpler than five
/// separate message classes for what's otherwise the same framing and
/// round-trip handling on both ends.
/// </summary>
public sealed class BridgeRequestMessage
{
    public BridgeMessageKind Kind { get; set; }

    /// <summary>
    /// <c>Fiddler.Session.id</c> -- <c>Clearinet.ProxyCore.Sessions.SessionStore</c>'s
    /// own ordinal for this exchange, passed straight through from
    /// <c>IExtensionAutoTamperHost</c>'s own parameter of the same name.
    /// Unused (left at 0) for <see cref="BridgeMessageKind.Capabilities"/>.
    /// </summary>
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
/// The legacy host's answer to a <see cref="BridgeRequestMessage"/>. Always
/// sent, even for a fire-and-observe hook (<see cref="BridgeMessageKind.RequestAfter"/>/
/// <see cref="BridgeMessageKind.ResponseAfter"/>) -- the client ignores its
/// <see cref="Request"/>/<see cref="Response"/> payload for those two kinds
/// (matching <c>IExtensionAutoTamperHost.RunRequestAfter</c>/<c>RunResponseAfter</c>'s
/// own <c>void</c> return), but still waits for the round trip to complete,
/// so every message kind shares one framing/timeout/error-handling path on
/// both ends rather than a fire-and-forget special case.
/// </summary>
public sealed class BridgeResponseMessage
{
    /// <summary>
    /// False only when the legacy host itself couldn't complete the call --
    /// e.g. it isn't finished loading extensions yet. NOT set to false when
    /// an individual <c>IAutoTamper</c> hook throws: that's caught and
    /// logged per extension, the same way <c>LoadedExtensionSet.RunSafely</c>
    /// already isolates one badly-behaved in-process extension from the
    /// others (see <c>SessionBridgeRunner</c>'s own remarks) -- a throwing
    /// extension still produces a normal, <c>Ok: true</c> response carrying
    /// whatever the request/response looked like up to that point.
    /// </summary>
    public bool Ok { get; set; } = true;

    /// <summary>Set when <see cref="Ok"/> is false, or when <see cref="BridgeMessageKind.Capabilities"/> is answered by a host with nothing loaded yet -- human-readable, logged, not parsed.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Meaningful only for <see cref="BridgeMessageKind.Capabilities"/> --
    /// how many loaded extensions implement <c>Fiddler.IAutoTamper</c> right
    /// now. Zero (matching an unreachable host, which never gets this far)
    /// is exactly what makes a mismatched/empty extensions folder a cheap
    /// no-op on the main app's side -- see <c>LegacyExtensionHostBridgeClient</c>.
    /// </summary>
    public int AutoTamperCount { get; set; }

    /// <summary>
    /// The (possibly extension-edited) request, echoed back for
    /// <see cref="BridgeMessageKind.RequestBefore"/>/<see cref="BridgeMessageKind.RequestAfter"/>
    /// -- only <c>RequestBefore</c>'s value is actually used by the client,
    /// matching <c>IExtensionAutoTamperHost.RunRequestBefore</c>'s own
    /// return type.
    /// </summary>
    public WireRequest? Request { get; set; }

    /// <summary>Same as <see cref="Request"/>, for <see cref="BridgeMessageKind.ResponseBefore"/>/<see cref="BridgeMessageKind.ResponseAfter"/>.</summary>
    public WireResponse? Response { get; set; }
}
