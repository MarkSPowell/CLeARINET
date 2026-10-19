using System.Diagnostics;
using Violens.ProxyCore.Sessions;

namespace Violens.ProxyCore.Proxy;

/// <summary>
/// Collects the moments one exchange passes through in
/// <c>InterceptingProxyListener</c> and turns them into
/// <see cref="SessionTimings"/>. Not thread-safe: one per request, used by
/// the connection's own loop.
/// </summary>
internal sealed class SessionTimingRecorder
{
    private readonly long _start = Stopwatch.GetTimestamp();
    private long? _connectStart;
    private long? _dnsEnd;
    private long? _connectEnd;
    private long? _sslEnd;
    private long? _sendStart;
    private long? _sendEnd;
    private long? _firstByte;
    private long? _end;
    private long _paused;
    private bool _dnsSeparate;

    public int ConnectionId { get; set; }

    public bool ConnectionReused { get; set; }

    public string? Proxy { get; set; }

    /// <summary>The TLS connection to the server this exchange uses, described when the timings are built.</summary>
    public UpstreamTlsProbe? TlsProbe { get; set; }

    public static long Now() => Stopwatch.GetTimestamp();

    /// <summary>Starting to open a new connection to the server (or upstream proxy).</summary>
    public void ConnectStarted() => _connectStart = Now();

    /// <summary>The server's (or proxy's) address has been looked up.</summary>
    public void DnsDone()
    {
        _dnsEnd = Now();
        _dnsSeparate = true;
    }

    /// <summary>The TCP connection (and any tunnel through another proxy) is open.</summary>
    public void Connected() => _connectEnd = Now();

    /// <summary>The TLS handshake with the server is done.</summary>
    public void TlsDone() => _sslEnd = Now();

    public void SendStarted() => _sendStart ??= Now();

    public void SendDone() => _sendEnd = Now();

    /// <summary>The response's first line and headers have arrived.</summary>
    public void FirstByte() => _firstByte ??= Now();

    public void Done() => _end ??= Now();

    /// <summary>Adds time spent paused at a breakpoint (from <see cref="Now"/> readings).</summary>
    public void AddPause(long from, long to) => _paused += to - from;

    /// <summary>
    /// The phases measured so far. <paramref name="answeredLocally"/> marks
    /// a response VioLens made itself: the time from the request arriving
    /// to the answer is then counted as waiting, and the server phases don't
    /// apply.
    /// </summary>
    public SessionTimings Build(bool answeredLocally = false)
    {
        var end = _end ?? Now();
        if (answeredLocally)
        {
            return new SessionTimings
            {
                Blocked = 0,
                Wait = Ms(_start, end),
                Receive = 0,
                Paused = Ms(0, _paused),
                AnsweredLocally = true,
            };
        }

        var reachedServer = _sendStart ?? _connectStart ?? end;
        var timings = new SessionTimings
        {
            Blocked = Ms(_start, _connectStart ?? reachedServer),
            Paused = Ms(0, _paused),
            ConnectionId = ConnectionId,
            ConnectionReused = ConnectionReused,
            Proxy = Proxy,
            Tls = TlsProbe?.Snapshot(),
        };

        if (_connectStart is { } connectStart)
        {
            var connected = _connectEnd ?? _sendStart ?? end;
            timings = timings with
            {
                Dns = _dnsSeparate && _dnsEnd is { } dnsEnd ? Ms(connectStart, dnsEnd) : SessionTimings.NotApplicable,
                Connect = Ms(_dnsSeparate && _dnsEnd is { } afterDns ? afterDns : connectStart, connected),
                Ssl = _sslEnd is { } sslEnd ? Ms(connected, sslEnd) : SessionTimings.NotApplicable,
            };

            // Anything between the connection being ready and sending
            // (none in practice) still counts as blocked rather than vanishing.
            var ready = _sslEnd ?? connected;
            if (_sendStart is { } sendStart && sendStart > ready)
            {
                timings = timings with { Blocked = timings.Blocked + Ms(ready, sendStart) };
            }
        }

        if (_sendStart is null)
        {
            // Never sent: the connection failed (a 502).
            return timings with { AnsweredLocally = true };
        }

        var sent = _sendEnd ?? _firstByte ?? end;
        var firstByte = _firstByte ?? end;
        return timings with
        {
            Send = Ms(_sendStart.Value, sent),
            Wait = Ms(sent, Math.Max(sent, firstByte)),
            Receive = Ms(Math.Max(sent, firstByte), Math.Max(firstByte, end)),
        };
    }

    private static double Ms(long from, long to) =>
        Math.Round(Math.Max(0, Stopwatch.GetElapsedTime(from, to).TotalMilliseconds), 3);
}
