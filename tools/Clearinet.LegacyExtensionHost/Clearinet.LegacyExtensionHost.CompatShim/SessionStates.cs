namespace Clearinet.CompatShim;

/// <summary>
/// <b>Flagged, not verified.</b> Referenced only as a parameter type --
/// <c>ContentBlock</c> calls <c>Session.set_state(Fiddler.SessionStates)</c>
/// (metadata confirms the TYPE, which is all binding requires) -- but which
/// specific enum member(s) it actually passes is baked into that DLL's IL
/// method body as a raw integer constant, which this project's metadata-only
/// research deliberately never reads (see the design doc's clean-room
/// section). The member names and underlying values below are this
/// project's own reasonable guess at Fiddler Classic's real session-state
/// concepts, not a confirmed reproduction of its actual enum layout --
/// <c>ContentBlock</c>'s call will still bind and execute either way (the
/// CLR doesn't validate that a passed int corresponds to a declared member),
/// but the specific state it ends up setting may not match what the
/// original author intended until this is checked against real behavior.
/// </summary>
public enum SessionStates
{
    Automatic = 0,
    RequestReadPending = 1,
    RequestSending = 2,
    RequestSent = 3,
    ResponseReadPending = 4,
    ResponseReceivingHeaders = 5,
    ResponseReceivingBody = 6,
    Done = 7,
    Aborted = 8,
    RequiresDNSResolution = 9,
}
