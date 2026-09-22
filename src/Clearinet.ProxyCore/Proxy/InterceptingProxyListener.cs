using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Clearinet.ProxyCore.Certificates;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>
/// A minimal HTTPS-intercepting proxy: just enough to prove the
/// certificate design out end to end -- accept a CONNECT tunnel, terminate
/// TLS toward the client with a freshly-signed leaf, open a fresh TLS
/// connection upstream, and surface the first decrypted request line.
///
/// This is deliberately not a full HTTP/1.1 engine yet. After logging the
/// first request line it relays bytes unmodified in both directions,
/// which is enough to prove decryption works without building the
/// session model, SAZ writer or inspector pipeline this early -- that's
/// the rest of Phase 1, once this spike confirms the certificate design
/// holds up against real traffic.
/// </summary>
public sealed class InterceptingProxyListener
{
    private readonly LeafCertificateProvider _leafProvider;
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;

    public InterceptingProxyListener(int port, LeafCertificateProvider leafProvider)
    {
        _leafProvider = leafProvider;
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

            await PumpFirstRequestAndRelayAsync(targetHost, clientTls, upstreamTls, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[proxy] Connection ended: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task PumpFirstRequestAndRelayAsync(
        string targetHost,
        SslStream clientTls,
        SslStream upstreamTls,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var read = await clientTls.ReadAsync(buffer.AsMemory(), cancellationToken);
        if (read > 0)
        {
            var firstLine = ExtractFirstLine(buffer.AsSpan(0, read));
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {targetHost} -> {firstLine}");
            await upstreamTls.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        var clientToUpstream = clientTls.CopyToAsync(upstreamTls, cancellationToken);
        var upstreamToClient = upstreamTls.CopyToAsync(clientTls, cancellationToken);

        try
        {
            await Task.WhenAll(clientToUpstream, upstreamToClient);
        }
        catch (Exception)
        {
            // A one-sided close is the normal way an HTTP/1.1 connection
            // ends here; nothing to act on.
        }
    }

    private static string ExtractFirstLine(ReadOnlySpan<byte> data)
    {
        var newlineIndex = data.IndexOf((byte)'\n');
        var lineBytes = newlineIndex >= 0 ? data[..newlineIndex] : data;
        return Encoding.ASCII.GetString(lineBytes).TrimEnd('\r');
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
