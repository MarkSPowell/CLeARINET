using Clearinet.ProxyCore.Http;

// SessionState lives one namespace up, in Clearinet.ProxyCore itself --
// spelled out explicitly here rather than relying on enclosing-namespace
// lookup, just to keep this unambiguous.
using Clearinet.ProxyCore;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// One fully-captured request/response pair. <see cref="State"/> uses the
/// Fiddler-Classic-shaped <see cref="SessionState"/> scaffolded in Phase 0;
/// right now a session is only ever recorded once it's entirely finished,
/// so it's always created in <see cref="SessionState.Done"/>. The
/// intermediate states (ReadingRequest, HandTamperRequest, ...) start
/// mattering once there's a live session list and inspectors/breakpoints to
/// drive them -- that's Phase 2, not this capture spike.
/// </summary>
public sealed record Session(
    int Id,
    string Host,
    DateTimeOffset StartedAt,
    CapturedRequest Request,
    CapturedResponse Response,
    SessionState State = SessionState.Done);
