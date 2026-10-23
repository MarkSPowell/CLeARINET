namespace Violens.ProxyCore.AutoResponder;

/// <summary>
/// The result of <see cref="AutoResponderRules.Evaluate"/>: either a final
/// action that answers the request locally, or a passthrough carrying any
/// delays, headers and flags from non-final rules that matched.
/// </summary>
public sealed class AutoResponderOutcome
{
    /// <summary>Nothing matched: forward the request unchanged.</summary>
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

    /// <summary>The final action, or null to pass the request through.</summary>
    public AutoResponderActionKind? FinalActionKind { get; }

    /// <summary>The file path or URL, for a final action that needs one.</summary>
    public string? Text { get; }

    /// <summary>The total of every matched <c>*delay:</c>.</summary>
    public int DelayMilliseconds { get; }

    /// <summary>Headers from matched <c>*header:</c> rules, in order. Only applied when passing through.</summary>
    public IReadOnlyList<(string Name, string Value)> HeadersToSet { get; }

    /// <summary>Flags from matched <c>*flag:</c> rules.</summary>
    public IReadOnlyList<(string Name, string Value)> FlagsToSet { get; }

    /// <summary>A <c>*bpu</c> rule matched: break before the request.</summary>
    public bool ForceBreakpointBeforeRequest { get; }

    /// <summary>A <c>*bpafter</c> rule matched: break after the response.</summary>
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
