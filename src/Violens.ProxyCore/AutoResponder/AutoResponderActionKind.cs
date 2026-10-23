namespace Violens.ProxyCore.AutoResponder;

/// <summary>What an <see cref="AutoResponderAction"/> does.</summary>
public enum AutoResponderActionKind
{
    /// <summary>Serve a local file as the response body (the default, unprefixed form).</summary>
    ServeFile,

    /// <summary>Fetch a different URL and serve its response.</summary>
    ProxyUrl,

    /// <summary>Fiddler's <c>*redir:</c>: respond with a redirect.</summary>
    Redirect,

    /// <summary>Fiddler's <c>*delay:####</c>: add latency; non-final.</summary>
    Delay,

    /// <summary>Fiddler's <c>*header:Name=Value</c>: set a header on the outgoing request; non-final.</summary>
    SetHeader,

    /// <summary>Fiddler's <c>*flag:Name=Value</c>: set a session flag; non-final.</summary>
    SetFlag,

    /// <summary>Fiddler's <c>*reset</c>: end the connection with a TCP RST.</summary>
    Reset,

    /// <summary>Fiddler's <c>*drop</c>: close the connection without a response.</summary>
    Drop,

    /// <summary>Fiddler's <c>*CORSPreflightAllow</c>: answer an OPTIONS preflight with permissive CORS headers.</summary>
    CorsPreflightAllow,

    /// <summary>Fiddler's <c>*exit</c>: stop evaluating rules and pass the request through.</summary>
    Exit,

    /// <summary>Fiddler's <c>*bpu</c>: break before the request.</summary>
    BreakBeforeRequest,

    /// <summary>Fiddler's <c>*bpafter</c>: break after the response.</summary>
    BreakAfterResponse,
}
