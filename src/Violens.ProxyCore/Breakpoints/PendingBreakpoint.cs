using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Breakpoints;

/// <summary>
/// A paused request or response. Edit it as raw text with
/// <see cref="TryEdit"/>, then <see cref="Resume"/> or <see cref="Abort"/>.
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

    /// <summary>A display number; the session has no Id until it's stored, after every breakpoint resolves.</summary>
    public int SessionOrdinal { get; }

    public BreakpointStage Stage { get; }

    public string Host { get; }

    /// <summary>The request. At the response stage it's shown for context but can't be edited.</summary>
    public CapturedRequest Request { get; private set; }

    /// <summary>Set only at the response stage.</summary>
    public CapturedResponse? Response { get; private set; }

    public string RawText { get; private set; }

    /// <summary>Why <see cref="RawText"/> doesn't parse, or null if it does. <see cref="Resume"/> needs it to parse.</summary>
    public string? ValidationError { get; private set; }

    public bool IsValid => ValidationError is null;

    /// <summary>
    /// Sets <see cref="RawText"/> to the new text even if it doesn't parse, so
    /// half-typed edits aren't lost. Returns whether it parsed.
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

    /// <summary>Sends the (possibly edited) message on. Throws if <see cref="RawText"/> doesn't parse.</summary>
    public void Resume()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(
                $"Can't resume: the edited text isn't valid HTTP yet ({ValidationError}).");
        }

        _resolution.TrySetResult(true);
    }

    /// <summary>Closes the connection instead.</summary>
    public void Abort() => _resolution.TrySetResult(false);

    /// <summary>
    /// Waits for Resume (true) or Abort (false). Throws
    /// <see cref="OperationCanceledException"/> if cancelled first.
    /// </summary>
    internal async Task<bool> WaitForResolutionAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _resolution.TrySetCanceled(cancellationToken));
        return await _resolution.Task.ConfigureAwait(false);
    }
}
