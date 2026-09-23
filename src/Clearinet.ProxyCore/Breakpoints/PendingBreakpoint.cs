using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Breakpoints;

/// <summary>
/// One paused request or response, sitting between "fully read" and "sent
/// onward" while something -- a desktop app's UI thread, a test, anything
/// -- decides what happens to it. Created and owned internally by
/// <see cref="BreakpointManager"/>; a caller only ever sees one through
/// <see cref="BreakpointManager.BreakpointHit"/> and acts on it via
/// <see cref="TryEdit"/>, <see cref="Resume"/> and <see cref="Abort"/>.
///
/// <see cref="RawText"/> is the same raw-text-editing model as Fiddler
/// Classic's TextView (see HttpMessageText's remarks): the whole message
/// as one editable blob, not per-field controls for method/URL/headers/body.
/// Editing it always updates <see cref="RawText"/> itself (so an in-progress,
/// currently-invalid edit is never silently discarded from the editor the
/// caller is looking at), but only updates the parsed <see cref="Request"/>/
/// <see cref="Response"/> that <see cref="Resume"/> will actually send when
/// the edit parses successfully -- see <see cref="ValidationError"/>.
/// </summary>
public sealed class PendingBreakpoint
{
    private readonly TaskCompletionSource<bool> _resolution =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal PendingBreakpoint(
        int sessionOrdinal, BreakpointStage stage, string host, CapturedRequest request, CapturedResponse? response)
    {
        SessionOrdinal = sessionOrdinal;
        Stage = stage;
        Host = host;
        Request = request;
        Response = response;
        RawText = stage == BreakpointStage.Request ? HttpMessageText.Format(request) : HttpMessageText.Format(response!);
    }

    /// <summary>
    /// A display-only counter, assigned when the breakpoint fires --
    /// distinct from a captured <c>Session.Id</c>, since that's only
    /// assigned once <c>Clearinet.ProxyCore.Sessions.SessionStore</c>
    /// records the session, which happens after every breakpoint on it has
    /// resolved.
    /// </summary>
    public int SessionOrdinal { get; }

    public BreakpointStage Stage { get; }

    public string Host { get; }

    /// <summary>
    /// The request, always present -- even at the response stage, where
    /// it's kept purely for display/context and isn't itself editable
    /// anymore (that breakpoint already resolved).
    /// </summary>
    public CapturedRequest Request { get; private set; }

    /// <summary>Set only when <see cref="Stage"/> is <see cref="BreakpointStage.Response"/>.</summary>
    public CapturedResponse? Response { get; private set; }

    public string RawText { get; private set; }

    /// <summary>
    /// Null when <see cref="RawText"/> currently parses to valid HTTP.
    /// <see cref="Resume"/> refuses to run while this is set -- the caller
    /// (a UI's Resume button, typically) should disable itself the same
    /// way.
    /// </summary>
    public string? ValidationError { get; private set; }

    public bool IsValid => ValidationError is null;

    /// <summary>
    /// Applies an edit. Always updates <see cref="RawText"/> to exactly
    /// what was passed in, whether or not it parses -- an invalid
    /// in-progress edit stays visible and editable rather than reverting
    /// out from under the person typing it. Returns whether it parsed.
    /// </summary>
    public bool TryEdit(string newRawText)
    {
        RawText = newRawText;

        bool ok;
        string? error;
        if (Stage == BreakpointStage.Request)
        {
            ok = HttpMessageText.TryParseRequest(newRawText, out var parsed, out error);
            if (ok)
            {
                Request = parsed!;
            }
        }
        else
        {
            ok = HttpMessageText.TryParseResponse(newRawText, out var parsed, out error);
            if (ok)
            {
                Response = parsed!;
            }
        }

        ValidationError = error;
        return ok;
    }

    /// <summary>
    /// Lets the (possibly edited) message continue on its way. Throws if
    /// the current <see cref="RawText"/> doesn't parse -- callers are
    /// expected to gate this behind <see cref="IsValid"/> (a disabled
    /// Resume button, say) rather than relying on this throw as the first
    /// check.
    /// </summary>
    public void Resume()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                $"Can't resume: the edited text isn't valid HTTP yet ({ValidationError}).");
        }

        _resolution.TrySetResult(true);
    }

    /// <summary>Ends the connection instead of letting the message continue.</summary>
    public void Abort() => _resolution.TrySetResult(false);

    /// <summary>
    /// Blocks the calling (proxy connection-handling) thread until
    /// <see cref="Resume"/> or <see cref="Abort"/> is called. Returns
    /// <see langword="true"/> for resume, <see langword="false"/> for
    /// abort. If <paramref name="cancellationToken"/> fires first (the
    /// connection or the whole proxy shutting down while this was still
    /// paused) this throws <see cref="OperationCanceledException"/>
    /// instead of returning -- same as any other cancellable await in this
    /// proxy, and the caller's existing catch-all handles it the same way.
    /// </summary>
    internal async Task<bool> WaitForResolutionAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _resolution.TrySetCanceled(cancellationToken));
        return await _resolution.Task.ConfigureAwait(false);
    }
}
