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
///
/// The name itself is a deliberate, on-the-record choice, not an accident:
/// Eric Lawrence has since said he regrets naming Fiddler's own equivalent
/// type <c>Session</c> ("there are so many different concepts of a
/// <c>Session</c> in web networking"), and that <c>Exchange</c> or
/// <c>Pair</c> would have been better -- see the "Lessons from Fiddler's
/// own history" section of the Project Plan and Goals doc. This type keeps
/// <c>Session</c> anyway, on purpose, since tenet 1 is API/usage
/// compatibility with Fiddler, and it's presumably what
/// ericlaw1979/Clearinet itself calls it too.
/// </summary>
public sealed record Session(
    int Id,
    string Host,
    DateTimeOffset StartedAt,
    CapturedRequest Request,
    CapturedResponse Response,
    SessionState State = SessionState.Done);
