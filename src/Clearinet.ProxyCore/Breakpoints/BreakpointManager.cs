using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Breakpoints;

/// <summary>
/// Coordinates Fiddler-Classic-style breakpoints: <see cref="Rules"/>
/// decides which requests/responses pause, and
/// <see cref="ApplyRequestBreakpointAsync"/>/<see cref="ApplyResponseBreakpointAsync"/>
/// are the two points <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c>
/// calls into from its connection-handling threads -- never a UI thread, so
/// <see cref="BreakpointHit"/>/<see cref="BreakpointResolved"/> subscribers
/// need to marshal back to their own UI thread themselves, the same way
/// Clearinet.ProxyCore.Sessions.SessionStore's SessionAdded event already
/// requires.
///
/// A default, untouched instance is a complete no-op:
/// <see cref="BreakpointRules.AnyActive"/> is checked before anything else,
/// so a caller that never sets a rule pays one boolean check per message
/// and nothing more -- no allocation, no risk of ever blocking.
/// </summary>
public sealed class BreakpointManager
{
    private int _ordinal;

    public BreakpointRules Rules { get; } = new();

    /// <summary>Fired (off the calling thread) the moment a session pauses.</summary>
    public event Action<PendingBreakpoint>? BreakpointHit;

    /// <summary>Fired once that same pause has been resumed, aborted, or cancelled.</summary>
    public event Action<PendingBreakpoint>? BreakpointResolved;

    /// <summary>
    /// Whether <see cref="ApplyRequestBreakpointAsync"/> would actually
    /// pause for this request, without pausing anything. Only looks at
    /// <c>Method</c>/<c>Target</c> (via <see cref="BreakpointRules.ShouldBreakBeforeRequest"/>),
    /// never the body, so this can be answered from a
    /// <see cref="Http1MessageReader.ReadRequestPreambleAsync"/> result
    /// before a single byte of the body has been read --
    /// <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c> uses
    /// exactly that to decide whether it needs to buffer the body for
    /// editing or can relay it straight through.
    /// </summary>
    public bool WouldBreakBeforeRequest(CapturedRequest request) =>
        Rules.AnyActive && Rules.ShouldBreakBeforeRequest(request);

    /// <summary>Same idea as <see cref="WouldBreakBeforeRequest"/>, for the response stage.</summary>
    public bool WouldBreakBeforeResponse(CapturedRequest request, CapturedResponse response) =>
        Rules.AnyActive && Rules.ShouldBreakBeforeResponse(request, response);

    /// <summary>
    /// Returns <paramref name="request"/> unchanged when no rule matches.
    /// Otherwise pauses until resumed (returning the edited request) or
    /// aborted/cancelled (throwing <see cref="BreakpointAbortedException"/>
    /// or <see cref="OperationCanceledException"/> respectively).
    /// </summary>
    public async Task<CapturedRequest> ApplyRequestBreakpointAsync(
        string host, CapturedRequest request, CancellationToken cancellationToken)
    {
        if (!WouldBreakBeforeRequest(request))
        {
            return request;
        }

        var pending = new PendingBreakpoint(NextOrdinal(), BreakpointStage.Request, host, request, response: null);
        var resumed = await RunAsync(pending, cancellationToken).ConfigureAwait(false);
        if (!resumed)
        {
            throw new BreakpointAbortedException(host, BreakpointStage.Request, request.Method, request.Target);
        }

        return pending.Request;
    }

    /// <summary>Same shape as <see cref="ApplyRequestBreakpointAsync"/>, for the response stage.</summary>
    public async Task<CapturedResponse> ApplyResponseBreakpointAsync(
        string host, CapturedRequest request, CapturedResponse response, CancellationToken cancellationToken)
    {
        if (!WouldBreakBeforeResponse(request, response))
        {
            return response;
        }

        var pending = new PendingBreakpoint(NextOrdinal(), BreakpointStage.Response, host, request, response);
        var resumed = await RunAsync(pending, cancellationToken).ConfigureAwait(false);
        if (!resumed)
        {
            throw new BreakpointAbortedException(host, BreakpointStage.Response, request.Method, request.Target);
        }

        return pending.Response!;
    }

    private async Task<bool> RunAsync(PendingBreakpoint pending, CancellationToken cancellationToken)
    {
        BreakpointHit?.Invoke(pending);
        try
        {
            return await pending.WaitForResolutionAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Runs on resume, abort, AND cancellation (an exception in the
            // try block still runs this) -- a subscriber's "remove this
            // from the pending list" handler shouldn't have to special-case
            // any of the three.
            BreakpointResolved?.Invoke(pending);
        }
    }

    private int NextOrdinal() => Interlocked.Increment(ref _ordinal);
}
