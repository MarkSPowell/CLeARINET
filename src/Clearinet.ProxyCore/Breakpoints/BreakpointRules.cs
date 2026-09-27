using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Breakpoints;

/// <summary>
/// What to pause on, in the same shape as Fiddler Classic's QuickExec
/// breakpoint commands: a global "break on everything" toggle per stage
/// (<c>Rules -&gt; Automatic Breakpoints</c>), plus one active value per
/// narrower condition (<c>bpu</c> URL-contains, <c>bpm</c> method,
/// <c>bps</c> response status). Fiddler keeps exactly one active value per
/// command rather than a list of rules; this mirrors that rather than
/// building a general rule-list engine nobody's asked for yet.
///
/// Plain mutable state, not an <c>INotifyPropertyChanged</c> view model --
/// this lives in Clearinet.ProxyCore, which has no UI-framework opinions
/// (see the IInspector contract's remarks for the same reasoning applied
/// to inspectors). Clearinet.DesktopUi's MainWindowViewModel wraps the
/// properties it exposes as bindable ones.
/// </summary>
public sealed class BreakpointRules
{
    public bool BreakOnAllRequests { get; set; }

    public bool BreakOnAllResponses { get; set; }

    /// <summary>Fiddler's <c>bpu</c>: pause a request whose target contains this.</summary>
    public string? RequestUrlContains { get; set; }

    /// <summary>Fiddler's <c>bpafter</c>: pause a response whose request target contains this.</summary>
    public string? ResponseUrlContains { get; set; }

    /// <summary>Fiddler's <c>bpm</c>: pause a request with this HTTP method.</summary>
    public string? RequestMethodEquals { get; set; }

    /// <summary>Fiddler's <c>bps</c>: pause a response with this status code.</summary>
    public int? ResponseStatusCodeEquals { get; set; }

    /// <summary>
    /// Cheap short-circuit for <see cref="BreakpointManager"/>: when
    /// nothing is configured, every call should cost one boolean check,
    /// not a walk through five individually-false conditions.
    /// </summary>
    public bool AnyActive =>
        BreakOnAllRequests || BreakOnAllResponses
        || !string.IsNullOrEmpty(RequestUrlContains)
        || !string.IsNullOrEmpty(ResponseUrlContains)
        || !string.IsNullOrEmpty(RequestMethodEquals)
        || ResponseStatusCodeEquals is not null;

    public bool ShouldBreakBeforeRequest(CapturedRequest request)
    {
        if (BreakOnAllRequests)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(RequestUrlContains) &&
            request.Target.Contains(RequestUrlContains, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrEmpty(RequestMethodEquals) &&
               string.Equals(request.Method, RequestMethodEquals, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldBreakBeforeResponse(CapturedRequest request, CapturedResponse response)
    {
        if (BreakOnAllResponses)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(ResponseUrlContains) &&
            request.Target.Contains(ResponseUrlContains, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ResponseStatusCodeEquals is { } status && response.StatusCode == status;
    }
}
