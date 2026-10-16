namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// How long each phase of one exchange took, in milliseconds -- the same
/// phases as HAR's <c>timings</c> object, and so as the waterfall in a
/// browser's developer tools:
/// <list type="bullet">
/// <item><see cref="Blocked"/>: from the request arriving to CLeARINET
/// starting to send it on (reading a buffered body, FiddlerScript and
/// extensions, a request breakpoint, choosing the route).</item>
/// <item><see cref="Dns"/>, <see cref="Connect"/>, <see cref="Ssl"/>:
/// opening a new connection to the server. <see cref="NotApplicable"/> (-1)
/// when an open connection was reused; DNS is also -1 when it's part of
/// <see cref="Connect"/> (a tunnel through another proxy).</item>
/// <item><see cref="Send"/>: sending the request (a body relayed as the
/// client uploads it counts here).</item>
/// <item><see cref="Wait"/>: from the request being sent to the first byte
/// of the response (time to first byte).</item>
/// <item><see cref="Receive"/>: reading the rest of the response, and
/// passing it on.</item>
/// </list>
/// <see cref="Paused"/> is how much of that was spent at a breakpoint
/// (already counted in <see cref="Blocked"/> or <see cref="Receive"/>),
/// so a slow-looking request can be told apart from a slow server.
/// </summary>
public sealed record SessionTimings
{
    public const double NotApplicable = -1;

    public double Blocked { get; init; } = NotApplicable;

    public double Dns { get; init; } = NotApplicable;

    public double Connect { get; init; } = NotApplicable;

    public double Ssl { get; init; } = NotApplicable;

    public double Send { get; init; } = NotApplicable;

    public double Wait { get; init; } = NotApplicable;

    public double Receive { get; init; } = NotApplicable;

    /// <summary>Time spent paused at a breakpoint, included in the phases above.</summary>
    public double Paused { get; init; }

    /// <summary>
    /// Which connection to the server (or upstream proxy) carried the
    /// exchange, numbered as CLeARINET opened them; 0 when unknown or none.
    /// </summary>
    public int ConnectionId { get; init; }

    /// <summary>True when the exchange reused a connection an earlier one opened.</summary>
    public bool ConnectionReused { get; init; }

    /// <summary>The upstream proxy the exchange went through, if any (e.g. "proxy.corp:8080").</summary>
    public string? Proxy { get; init; }

    /// <summary>
    /// True when CLeARINET answered the request itself (AutoResponder, an
    /// extension, its own page, or a 502 when the server couldn't be
    /// reached), so the server phases don't apply.
    /// </summary>
    public bool AnsweredLocally { get; init; }

    /// <summary>The whole exchange: the sum of the phases that apply.</summary>
    public double Total =>
        new[] { Blocked, Dns, Connect, Ssl, Send, Wait, Receive }.Where(phase => phase > 0).Sum();

    /// <summary>The phases in order, with their HAR names, for drawing a waterfall.</summary>
    public IReadOnlyList<(string Name, double Milliseconds)> Phases =>
    [
        ("blocked", Blocked),
        ("dns", Dns),
        ("connect", Connect),
        ("ssl", Ssl),
        ("send", Send),
        ("wait", Wait),
        ("receive", Receive),
    ];
}
