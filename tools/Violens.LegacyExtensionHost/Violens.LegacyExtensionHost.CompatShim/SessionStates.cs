namespace Violens.CompatShim;

/// <summary>
/// Only the type is known (ContentBlock passes one to <c>Session.state</c>).
/// These names and values are a guess at Fiddler's, so the state
/// ContentBlock sets may not be the one it meant.
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
