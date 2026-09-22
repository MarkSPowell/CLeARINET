using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>
/// An HTTPS-intercepting proxy: accepts a CONNECT tunnel, terminates TLS
/// toward the client with a freshly-signed leaf, opens a fresh TLS
/// connection upstream, and reads each request/response pair on the
/// connection as a full HTTP/1.1 message (see <see cref="Http1MessageReader"/>),
/// capturing it into <see cref="SessionStore"/> while relaying the exact
/// original bytes through unmodified.
///
/// Still deliberately narrow: one connection is handled as a strict
/// request-then-response ping-pong (no pipelining), and a handful of
/// framing edge cases aren't covered yet -- see Http1MessageReader's
/// remarks. That's enough to prove the session model and, next, the SAZ
/// writer against real traffic, which is the rest of Phase 1.
/// </summary>
public sealed class InterceptingProxyListener
{
    private readonly LeafCertificateProvider _leafProvider;
    private readonly SessionStore _sessionStore;
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;

    public InterceptingProxyListener(int port, LeafCertificateProvider leafProvider, SessionStore sessionStore)
    {
        _leafProvider = leafProvider;
        _sessionStore = sessionStore;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        client.NoDelay = true;

        try
        {
            using var clientStream = client.GetStream();
            var (method, target) = await ReadRequestLineAndHeadersAsync(clientStream, cancellationToken);

            if (method is null || !string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"[proxy] Ignoring non-CONNECT request ({method ?? "?"} {target}); this spike only handles HTTPS tunnels.");
                return;
            }

            var (targetHost, targetPort) = ParseConnectTarget(target!);

            var established = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
            await clientStream.WriteAsync(established, cancellationToken);

            using var clientTls = new SslStream(clientStream, leaveInnerStreamOpen: false);
            var serverOptions = new SslServerAuthenticationOptions
            {
                ServerCertificateSelectionCallback = (_, sniHostName) =>
                    _leafProvider.GetCertificateFor(string.IsNullOrEmpty(sniHostName) ? targetHost : sniHostName),
                // Let the OS pick the best mutually-supported protocol
                // (TLS 1.2/1.3) rather than pinning one here.
                EnabledSslProtocols = SslProtocols.None,
            };
            await clientTls.AuthenticateAsServerAsync(serverOptions, cancellationToken);

            using var upstreamClient = new TcpClient();
            await upstreamClient.ConnectAsync(targetHost, targetPort, cancellationToken);
            using var upstreamStream = upstreamClient.GetStream();
            using var upstreamTls = new SslStream(upstreamStream, leaveInnerStreamOpen: false);
            // Deliberately using default certificate validation here: this
            // proxy should surface a real upstream cert problem, not hide
            // it, even though it's standing in the middle of the
            // connection for the client's side.
            await upstreamTls.AuthenticateAsClientAsync(targetHost);

            await PumpSessionsAsync(targetHost, clientTls, upstreamTls, _sessionStore, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[proxy] Connection ended: {DescribeException(ex)}");
        }
    }

    private static string DescribeException(Exception ex)
    {
        // AuthenticationException's own Message is almost always the
        // unhelpful "Authentication failed, see inner exception." -- the
        // actual reason lives in InnerException (sometimes nested a
        // couple of levels deep), so walk the chain instead of printing
        // just the outer wrapper.
        var description = $"{ex.GetType().Name}: {ex.Message}";
        var inner = ex.InnerException;
        while (inner is not null)
        {
            description += $" ---> {inner.GetType().Name}: {inner.Message}";
            inner = inner.InnerException;
        }

        return description;
    }

    private static async Task PumpSessionsAsync(
        string targetHost,
        SslStream clientTls,
        SslStream upstreamTls,
        SessionStore sessionStore,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var startedAt = DateTimeOffset.Now;
            var request = await Http1MessageReader.ReadRequestAsync(clientTls, upstreamTls, cancellationToken);
            if (request is null)
            {
                // The client closed the connection between requests -- the
                // normal way a keep-alive HTTP/1.1 connection ends.
                return;
            }

            var isHeadRequest = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            var response = await Http1MessageReader.ReadResponseAsync(
                upstreamTls, clientTls, isHeadRequest, cancellationToken);
            if (response is null)
            {
                Console.WriteLine(
                    $"[proxy] {targetHost}: upstream closed the connection before answering {request.Method} {request.Target}.");
                return;
            }

            var session = sessionStore.Add(targetHost, startedAt, request, response);
            Console.WriteLine(
                $"[{startedAt:HH:mm:ss}] #{session.Id} {response.StatusCode} {request.Method} https://{targetHost}{request.Target} " +
                $"({request.Body.Length} B req, {response.Body.Length} B resp)");

            if (response.StatusCode == 101)
            {
                // 101 Switching Protocols -- the connection just stopped
                // being HTTP/1.1 (WebSocket is by far the common case here).
                // Trying to read another request off it would mean parsing
                // binary frame data as if it were an HTTP start line, which
                // fails outright, and even before it fails it can't work
                // right: a request-then-response loop is fundamentally
                // half-duplex, while what's needed from here on is a full
                // duplex, unparsed relay in both directions at once. Fall
                // back to exactly that for the rest of this connection's
                // life -- the same raw pump this spike used everywhere
                // before it understood HTTP/1.1 at all.
                await RelayRawBytesUntilClosedAsync(clientTls, upstreamTls, cancellationToken);
                return;
            }
        }
    }

    private static async Task RelayRawBytesUntilClosedAsync(
        SslStream clientTls, SslStream upstreamTls, CancellationToken cancellationToken)
    {
        var clientToUpstream = clientTls.CopyToAsync(upstreamTls, cancellationToken);
        var upstreamToClient = upstreamTls.CopyToAsync(clientTls, cancellationToken);

        try
        {
            await Task.WhenAll(clientToUpstream, upstreamToClient);
        }
        catch (Exception)
        {
            // A one-sided close is the normal way an upgraded connection
            // like a WebSocket ends here; nothing to act on.
        }
    }

    private static async Task<(string? Method, string? Target)> ReadRequestLineAndHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken);
        if (line is null)
        {
            return (null, null);
        }

        var parts = line.Split(' ', 3);
        var method = parts.Length > 0 ? parts[0] : null;
        var target = parts.Length > 1 ? parts[1] : null;

        // Drain the rest of the request headers up to the blank line;
        // CONNECT never carries a body.
        while (true)
        {
            var headerLine = await ReadLineAsync(stream, cancellationToken);
            if (string.IsNullOrEmpty(headerLine))
            {
                break;
            }
        }

        return (method, target);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Byte-at-a-time on purpose: the only thing read this way is the
        // small, plaintext CONNECT request that precedes the TLS
        // handshake, so simplicity matters more than throughput here.
        var bytes = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(single.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            if (single[0] != (byte)'\r')
            {
                bytes.Add(single[0]);
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static (string Host, int Port) ParseConnectTarget(string target)
    {
        var separatorIndex = target.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            return (target, 443);
        }

        var host = target[..separatorIndex];
        var portText = target[(separatorIndex + 1)..];
        return int.TryParse(portText, out var port) ? (host, port) : (host, 443);
    }
}
