namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// What <see cref="AutoResponderRules.Evaluate"/> decided for one request:
/// either a final action that answers the request itself (<see cref="FinalActionKind"/>
/// non-null), or a passthrough -- possibly still carrying accumulated
/// non-final effects (<see cref="DelayMilliseconds"/>, <see cref="HeadersToSet"/>)
/// from rules that matched before evaluation stopped, whether it stopped
/// because a rule's action was final or because the rule list simply ran
/// out.
///
/// <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c> is the only
/// consumer: <see cref="FinalActionKind"/> null means "forward to the real
/// server," applying <see cref="DelayMilliseconds"/>/<see cref="HeadersToSet"/>
/// first; non-null means "answer this locally, never touch the real
/// server" -- see that class's own remarks on why the upstream connection
/// has to be made lazily for that second case to actually mean what it
/// says.
/// </summary>
public sealed class AutoResponderOutcome
{
    /// <summary>The plain "nothing matched, nothing to apply" case -- forward the request exactly as captured.</summary>
    public static readonly AutoResponderOutcome PassThrough = new(
        finalActionKind: null, text: null, delayMilliseconds: 0,
        headersToSet: [], flagsToSet: [],
        forceBreakpointBeforeRequest: false, forceBreakpointAfterResponse: false);

    private AutoResponderOutcome(
        AutoResponderActionKind? finalActionKind,
        string? text,
        int delayMilliseconds,
        IReadOnlyList<(string Name, string Value)> headersToSet,
        IReadOnlyList<(string Name, string Value)> flagsToSet,
        bool forceBreakpointBeforeRequest,
        bool forceBreakpointAfterResponse)
    {
        FinalActionKind = finalActionKind;
        Text = text;
        DelayMilliseconds = delayMilliseconds;
        HeadersToSet = headersToSet;
        FlagsToSet = flagsToSet;
        ForceBreakpointBeforeRequest = forceBreakpointBeforeRequest;
        ForceBreakpointAfterResponse = forceBreakpointAfterResponse;
    }

    /// <summary>
    /// Null means passthrough. Otherwise one of the final kinds from
    /// <see cref="AutoResponderActionKind"/> -- never <see cref="AutoResponderActionKind.Delay"/>,
    /// <see cref="AutoResponderActionKind.SetHeader"/>, or
    /// <see cref="AutoResponderActionKind.SetFlag"/>, which by definition
    /// never stop evaluation on their own (see <see cref="AutoResponderAction.IsFinal"/>).
    /// </summary>
    public AutoResponderActionKind? FinalActionKind { get; }

    /// <summary>The file path / fetch URL / redirect URL for a <see cref="FinalActionKind"/> that needs one.</summary>
    public string? Text { get; }

    /// <summary>Summed across every matched <c>*delay:</c> rule before evaluation stopped.</summary>
    public int DelayMilliseconds { get; }

    /// <summary>Accumulated from every matched <c>*header:</c> rule, in match order -- only meaningful when <see cref="FinalActionKind"/> is null (see the class remarks on why a header override is moot once a final action bypasses the real server entirely).</summary>
    public IReadOnlyList<(string Name, string Value)> HeadersToSet { get; }

    /// <summary>
    /// Accumulated from every matched <c>*flag:</c> rule. Nothing in
    /// CLeARINET reads a session's flags today -- <c>SazWriter</c>'s own
    /// <c>&lt;SessionFlags /&gt;</c> element is still always empty (see its
    /// remarks) -- so this is parsed and carried faithfully but currently
    /// has no effect. Recorded here rather than silently dropped so a
    /// future flags consumer (or a SAZ export that starts populating
    /// <c>SessionFlags</c> for real) has somewhere to read them from
    /// without another AutoResponder change.
    /// </summary>
    public IReadOnlyList<(string Name, string Value)> FlagsToSet { get; }

    /// <summary>Fiddler's <c>*bpu</c> fired -- treat this request as breaking, the same as <c>BreakpointManager.WouldBreakBeforeRequest</c> returning true.</summary>
    public bool ForceBreakpointBeforeRequest { get; }

    /// <summary>Fiddler's <c>*bpafter</c> fired -- same idea as <see cref="ForceBreakpointBeforeRequest"/>, at the response stage.</summary>
    public bool ForceBreakpointAfterResponse { get; }

    internal static AutoResponderOutcome Passthrough(
        int delayMilliseconds, IReadOnlyList<(string Name, string Value)> headersToSet, IReadOnlyList<(string Name, string Value)> flagsToSet) =>
        delayMilliseconds == 0 && headersToSet.Count == 0 && flagsToSet.Count == 0
            ? PassThrough
            : new AutoResponderOutcome(null, null, delayMilliseconds, headersToSet, flagsToSet, false, false);

    internal static AutoResponderOutcome Final(
        AutoResponderActionKind kind,
        string? text,
        int delayMilliseconds,
        IReadOnlyList<(string Name, string Value)> headersToSet,
        IReadOnlyList<(string Name, string Value)> flagsToSet) =>
        new(kind, text, delayMilliseconds, headersToSet, flagsToSet, false, false);

    internal static AutoResponderOutcome Breakpoint(bool beforeRequest, int delayMilliseconds, IReadOnlyList<(string Name, string Value)> headersToSet, IReadOnlyList<(string Name, string Value)> flagsToSet) =>
        new(
            beforeRequest ? AutoResponderActionKind.BreakBeforeRequest : AutoResponderActionKind.BreakAfterResponse,
            text: null, delayMilliseconds, headersToSet, flagsToSet,
            forceBreakpointBeforeRequest: beforeRequest,
            forceBreakpointAfterResponse: !beforeRequest);
}
