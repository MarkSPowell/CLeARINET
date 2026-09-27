namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// Fiddler-Classic-style AutoResponder: an ordered list of <see cref="AutoResponderRule"/>s,
/// evaluated top to bottom against a request's method and URL. See
/// <see cref="Evaluate"/> for the matching/final-vs-non-final semantics
/// this follows.
///
/// Plain mutable state, not an <c>INotifyPropertyChanged</c> view model --
/// same reasoning as <c>Clearinet.ProxyCore.Breakpoints.BreakpointRules</c>.
/// Clearinet.DesktopUi's MainWindowViewModel wraps whichever parts it
/// exposes as bindable ones.
/// </summary>
public sealed class AutoResponderRules
{
    /// <summary>
    /// The single global on/off switch, separate from each rule's own
    /// <see cref="AutoResponderRule.IsEnabled"/> -- matches real Fiddler's
    /// own two-level toggle (a whole panel you can disable at once, plus
    /// per-rule checkboxes underneath it). Defaults to <see langword="false"/>:
    /// unlike breakpoints (which only ever pause, never change behavior
    /// unless something is actually watching for it), an AutoResponder rule
    /// left enabled by accident would silently start intercepting real
    /// traffic the next time the proxy starts -- opt-in is the safer
    /// default.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Ordered top to bottom -- rule order is match priority, exactly like
    /// real Fiddler's own AutoResponder list. A plain <see cref="List{T}"/>
    /// rather than an immutable collection since a host UI needs to
    /// add/remove/reorder rules in place.
    /// </summary>
    public List<AutoResponderRule> Rules { get; } = [];

    /// <summary>
    /// Cheap short-circuit for <c>InterceptingProxyListener</c>: disabled or
    /// empty costs one check, not a walk through an empty or ignored list.
    /// Deliberately does NOT check whether any individual rule is itself
    /// enabled -- that's still a real, if slightly wasted, per-request
    /// <see cref="Evaluate"/> call, but keeping this a pure O(1) count check
    /// (matching <c>BreakpointRules.AnyActive</c>'s own shape) is worth
    /// more than trimming that one edge case.
    /// </summary>
    public bool AnyActive => IsEnabled && Rules.Count > 0;

    /// <summary>
    /// Walks <see cref="Rules"/> in order against <paramref name="method"/>/
    /// <paramref name="url"/>. A disabled rule (<see cref="AutoResponderRule.IsEnabled"/>
    /// false) is skipped as if it weren't in the list at all. For every
    /// rule that matches: a non-final action (<c>*delay:</c>/<c>*header:</c>/
    /// <c>*flag:</c>) accumulates its effect and evaluation continues to the
    /// next rule; a final action stops evaluation right there and becomes
    /// this call's result. Reaching the end of the list without a final
    /// action match is a passthrough, still carrying whatever non-final
    /// effects did accumulate along the way.
    /// </summary>
    public AutoResponderOutcome Evaluate(string method, string url)
    {
        if (!IsEnabled)
        {
            return AutoResponderOutcome.PassThrough;
        }

        var delayMilliseconds = 0;
        var headersToSet = new List<(string Name, string Value)>();
        var flagsToSet = new List<(string Name, string Value)>();

        foreach (var rule in Rules)
        {
            if (!rule.IsEnabled)
            {
                continue;
            }

            var match = AutoResponderMatch.Parse(rule.MatchPattern);
            if (!match.Matches(method, url))
            {
                continue;
            }

            var action = AutoResponderAction.Parse(rule.Action);
            switch (action.Kind)
            {
                case AutoResponderActionKind.Delay:
                    delayMilliseconds += action.Milliseconds;
                    continue;

                case AutoResponderActionKind.SetHeader:
                    headersToSet.Add((action.Name!, action.Value!));
                    continue;

                case AutoResponderActionKind.SetFlag:
                    flagsToSet.Add((action.Name!, action.Value!));
                    continue;

                case AutoResponderActionKind.Exit:
                    // Final, but produces no response of its own -- stop
                    // here and pass through with whatever accumulated so
                    // far, exactly as if the rule list ended at this point.
                    return AutoResponderOutcome.Passthrough(delayMilliseconds, headersToSet, flagsToSet);

                case AutoResponderActionKind.BreakBeforeRequest:
                    return AutoResponderOutcome.Breakpoint(beforeRequest: true, delayMilliseconds, headersToSet, flagsToSet);

                case AutoResponderActionKind.BreakAfterResponse:
                    return AutoResponderOutcome.Breakpoint(beforeRequest: false, delayMilliseconds, headersToSet, flagsToSet);

                default:
                    return AutoResponderOutcome.Final(action.Kind, action.Text, delayMilliseconds, headersToSet, flagsToSet);
            }
        }

        return AutoResponderOutcome.Passthrough(delayMilliseconds, headersToSet, flagsToSet);
    }
}
