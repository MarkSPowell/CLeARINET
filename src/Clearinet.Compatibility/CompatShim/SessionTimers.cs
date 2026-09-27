namespace Clearinet.CompatShim;

/// <summary>
/// Fiddler's per-session timestamps. Field names, types and meanings are
/// from the published object model (fiddlerbook.com, "SessionTimers
/// Fields"). They're public fields, not properties, as in Fiddler, so an
/// extension's chained assignments (<c>t.A = t.B = value;</c>) compile
/// unchanged.
///
/// CLeARINET's own native session keeps only a start time today, so when a
/// shim <see cref="Session"/> is handed to the host, only the earliest
/// request-side timestamp survives (see <c>ShimSessionConverter</c>).
/// </summary>
public class SessionTimers
{
    /// <summary>The time at which the client's HTTP connection to the proxy was established.</summary>
    public DateTime ClientConnected;

    /// <summary>The time at which the request's first Send() to the proxy completes.</summary>
    public DateTime ClientBeginRequest;

    /// <summary>The time at which the request headers were received.</summary>
    public DateTime FiddlerGotRequestHeaders;

    /// <summary>The time at which the request to the proxy completes.</summary>
    public DateTime ClientDoneRequest;

    /// <summary>The time at which the server connection has been established.</summary>
    public DateTime ServerConnected;

    /// <summary>The time at which the proxy begins sending the HTTP request to the server.</summary>
    public DateTime FiddlerBeginRequest;

    /// <summary>The time at which the proxy has completed sending the HTTP request to the server.</summary>
    public DateTime ServerGotRequest;

    /// <summary>The time at which the proxy receives the first byte of the server's response.</summary>
    public DateTime ServerBeginResponse;

    /// <summary>The time at which the proxy received the server's headers.</summary>
    public DateTime FiddlerGotResponseHeaders;

    /// <summary>The time at which the proxy has completed receipt of the server's response.</summary>
    public DateTime ServerDoneResponse;

    /// <summary>The time at which the proxy has begun sending the response to the client.</summary>
    public DateTime ClientBeginResponse;

    /// <summary>The time at which the proxy has completed sending the response to the client.</summary>
    public DateTime ClientDoneResponse;

    /// <summary>Milliseconds spent determining which gateway should be used.</summary>
    public int GatewayDeterminationTime;

    /// <summary>Milliseconds spent waiting for DNS.</summary>
    public int DNSTime;

    /// <summary>Milliseconds spent waiting for the server TCP/IP connection.</summary>
    public int TCPConnectTime;

    /// <summary>Milliseconds elapsed while performing the HTTPS handshake.</summary>
    public int HTTPSHandshakeTime;
}
