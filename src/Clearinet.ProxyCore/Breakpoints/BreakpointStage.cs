namespace Clearinet.ProxyCore.Breakpoints;

/// <summary>
/// Which side of a session a <see cref="PendingBreakpoint"/> paused --
/// matches the HandTamperRequest/HandTamperResponse states already
/// scaffolded in <c>Clearinet.ProxyCore.SessionState</c> (Fiddler
/// Classic's own session state machine), though this MVP doesn't yet
/// drive a captured <c>Session</c>'s state through those states -- a
/// breakpoint pauses before a <c>Session</c> object even exists (it's
/// only ever recorded once fully captured), so for now this enum is the
/// only place that distinction lives.
/// </summary>
public enum BreakpointStage
{
    Request,
    Response,
}
