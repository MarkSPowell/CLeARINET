namespace Violens.ProxyCore;

/// <summary>
/// Session lifecycle states, named after Fiddler Classic's
/// <c>Session.state</c> so ported scripts and extensions keep working.
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
