using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Breakpoints;

/// <summary>
/// What to pause on, as in Fiddler Classic: break-on-everything per stage,
/// plus one value each for <c>bpu</c>, <c>bpafter</c>, <c>bpm</c> and
/// <c>bps</c>.
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

    /// <summary>Whether any rule is set.</summary>
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
