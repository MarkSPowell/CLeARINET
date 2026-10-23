using Violens.ProxyCore.Http;

using Violens.ProxyCore;

namespace Violens.ProxyCore.Sessions;

/// <summary>
/// One captured request/response pair. Named <c>Session</c> to match
/// Fiddler, though its author has said he regrets the name.
/// <see cref="Flags"/> holds Fiddler-style string flags (<c>ui-backcolor</c>
/// and so on), null when there are none. <see cref="Scheme"/> is "https" or
/// "http"; either way the request target is the path and query (see
/// <c>SessionUrl.Of</c> for the full URL).
/// </summary>
public sealed record Session(
    int Id,
    string Host,
    DateTimeOffset StartedAt,
    CapturedRequest Request,
    CapturedResponse Response,
    SessionState State = SessionState.Done,
    IReadOnlyDictionary<string, string>? Flags = null,
    string Scheme = "https")
{
    /// <summary>
    /// The flag set on a session whose request body was larger than the
    /// listener keeps (see <c>InterceptingProxyListener.MaxCapturedBodyBytes</c>):
    /// it was relayed in full, but only its start is in
    /// <see cref="CapturedRequest.Body"/>. Its value says how much. Shows in the Notes tab.
    /// </summary>
    public const string RequestBodyTruncatedFlag = "x-request-body-truncated";

    /// <summary>As <see cref="RequestBodyTruncatedFlag"/>, for the response body.</summary>
    public const string ResponseBodyTruncatedFlag = "x-response-body-truncated";

    /// <summary>
    /// The original six-argument constructor, kept so extensions compiled
    /// before <see cref="Flags"/> existed still load (see
    /// SessionBinaryCompatibilityTests).
    /// </summary>
    public Session(int Id, string Host, DateTimeOffset StartedAt, CapturedRequest Request, CapturedResponse Response, SessionState State)
        : this(Id, Host, StartedAt, Request, Response, State, null, "https")
    {
    }

    /// <summary>
    /// The seven-argument constructor from before <see cref="Scheme"/> was
    /// added, kept for the same reason (an HTTPS session).
    /// </summary>
    public Session(int Id, string Host, DateTimeOffset StartedAt, CapturedRequest Request, CapturedResponse Response, SessionState State, IReadOnlyDictionary<string, string>? Flags)
        : this(Id, Host, StartedAt, Request, Response, State, Flags, "https")
    {
    }

    /// <summary>The seven-value deconstruction from before <see cref="Scheme"/>, kept for the same reason.</summary>
    public void Deconstruct(
        out int Id, out string Host, out DateTimeOffset StartedAt, out CapturedRequest Request, out CapturedResponse Response, out SessionState State,
        out IReadOnlyDictionary<string, string>? Flags)
    {
        Deconstruct(out Id, out Host, out StartedAt, out Request, out Response, out State);
        Flags = this.Flags;
    }

    /// <summary>
    /// How long each phase of the exchange took (see
    /// <see cref="SessionTimings"/>), or null when that isn't known (a
    /// session imported from a file that doesn't record it).
    /// </summary>
    public SessionTimings? Timings { get; init; }

    /// <summary>
    /// For a request that opened a WebSocket connection (status 101), the
    /// messages sent each way, added as they pass; otherwise null.
    /// </summary>
    public WebSockets.WebSocketLog? WebSocket { get; init; }

    /// <summary>The original six-value deconstruction, kept for the same reason.</summary>
    public void Deconstruct(out int Id, out string Host, out DateTimeOffset StartedAt, out CapturedRequest Request, out CapturedResponse Response, out SessionState State)
    {
        Id = this.Id;
        Host = this.Host;
        StartedAt = this.StartedAt;
        Request = this.Request;
        Response = this.Response;
        State = this.State;
    }
}
