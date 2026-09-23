namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// What an <see cref="AutoResponderAction"/> actually does. See
/// <see cref="AutoResponderAction.IsFinal"/> for which of these stop rule
/// evaluation outright versus just adjusting the request in passing.
/// </summary>
public enum AutoResponderActionKind
{
    /// <summary>Serve a local file's contents as the response body. The bare, unprefixed default -- see <see cref="AutoResponderAction"/>.</summary>
    ServeFile,

    /// <summary>Fetch a different URL and serve <em>its</em> response instead of the real one.</summary>
    ProxyUrl,

    /// <summary>Fiddler's <c>*redir:</c> -- respond with an HTTP redirect to another URL.</summary>
    Redirect,

    /// <summary>Fiddler's <c>*delay:####</c> -- non-final; adds latency before whatever happens next.</summary>
    Delay,

    /// <summary>Fiddler's <c>*header:Name=Value</c> -- non-final; overrides/adds a header on the real outgoing request.</summary>
    SetHeader,

    /// <summary>Fiddler's <c>*flag:Name=Value</c> -- non-final; see <see cref="AutoResponderOutcome.FlagsToSet"/>'s own remarks on why nothing consumes these yet.</summary>
    SetFlag,

    /// <summary>Fiddler's <c>*reset</c> -- ends the connection via TCP RST instead of answering.</summary>
    Reset,

    /// <summary>Fiddler's <c>*drop</c> -- ends the connection with no response and no RST.</summary>
    Drop,

    /// <summary>Fiddler's <c>*CORSPreflightAllow</c> -- answers an OPTIONS preflight with permissive CORS headers.</summary>
    CorsPreflightAllow,

    /// <summary>Fiddler's <c>*exit</c> -- stops evaluating further rules; the request passes through as if none of the remaining rules existed.</summary>
    Exit,

    /// <summary>Fiddler's <c>*bpu</c> -- hands off to the existing breakpoint pipeline instead of answering itself.</summary>
    BreakBeforeRequest,

    /// <summary>Fiddler's <c>*bpafter</c> -- same idea as <see cref="BreakBeforeRequest"/>, at the response stage.</summary>
    BreakAfterResponse,
}
