namespace Clearinet.ProxyCore;

/// <summary>
/// The lifecycle states a captured session moves through as it is read from
/// the client, optionally hand- or auto-tampered, sent to the server, and
/// read back. Modeled after the state machine documented for Fiddler
/// Classic's <c>Session.state</c> property, so that ported scripts and
/// extensions relying on these names keep working.
///
/// See the "Extensibility and core API surface" section of the CLeARINET
/// project plan for the source and reasoning; this is placeholder scaffolding
/// for Phase 1, not the final session model.
/// </summary>
public enum SessionState
{
    /// <summary>Object created but nothing's happening yet.</summary>
    Created,

    /// <summary>Thread is reading the HTTP request.</summary>
    ReadingRequest,

    /// <summary>Auto-tamper request pass 1 (extensions only).</summary>
    AutoTamperRequestBefore,

    /// <summary>User can tamper via inspectors/breakpoints.</summary>
    HandTamperRequest,

    /// <summary>Auto-tamper request pass 2 (extensions only).</summary>
    AutoTamperRequestAfter,

    /// <summary>Thread is sending the request to the server.</summary>
    SendingRequest,

    /// <summary>Thread is reading the HTTP response.</summary>
    ReadingResponse,

    /// <summary>Auto-tamper response pass 1 (extensions only).</summary>
    AutoTamperResponseBefore,

    /// <summary>User can tamper via inspectors/breakpoints.</summary>
    HandTamperResponse,

    /// <summary>Auto-tamper response pass 2 (extensions only).</summary>
    AutoTamperResponseAfter,

    /// <summary>Sending the response to the client application.</summary>
    SendingResponse,

    /// <summary>Session is complete and kept for archival purposes only.</summary>
    Done,

    /// <summary>Session was aborted (client cancel, fatal error, etc.).</summary>
    Aborted,
}
