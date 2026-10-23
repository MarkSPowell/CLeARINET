using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Breakpoints;

/// <summary>
/// Pauses requests and responses that match <see cref="Rules"/>. Called from
/// connection threads, so event subscribers must marshal to their own UI
/// thread. Costs one check per message when no rule is set.
/// </summary>
public sealed class BreakpointManager
{
    private int _ordinal;

    public BreakpointRules Rules { get; } = new();

    /// <summary>Raised when a session pauses.</summary>
    public event Action<PendingBreakpoint>? BreakpointHit;

    /// <summary>Raised when a pause is resumed, aborted or cancelled.</summary>
    public event Action<PendingBreakpoint>? BreakpointResolved;

    /// <summary>
    /// Whether this request would pause. Looks only at the method and target,
    /// so the listener can decide before reading the body whether to buffer it.
    /// </summary>
    public bool WouldBreakBeforeRequest(CapturedRequest request) =>
        Rules.AnyActive && Rules.ShouldBreakBeforeRequest(request);

    /// <summary>Whether this response would pause.</summary>
    public bool WouldBreakBeforeResponse(CapturedRequest request, CapturedResponse response) =>
        Rules.AnyActive && Rules.ShouldBreakBeforeResponse(request, response);

    /// <summary>
    /// Returns the request unchanged when no rule matches. Otherwise pauses
    /// until resumed (returning the edited request), aborted
    /// (<see cref="BreakpointAbortedException"/>) or cancelled.
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

    /// <summary>The response-stage counterpart of <see cref="ApplyRequestBreakpointAsync"/>.</summary>
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
            // Runs for resume, abort and cancellation alike.
            BreakpointResolved?.Invoke(pending);
        }
    }

    private int NextOrdinal() => Interlocked.Increment(ref _ordinal);
}
