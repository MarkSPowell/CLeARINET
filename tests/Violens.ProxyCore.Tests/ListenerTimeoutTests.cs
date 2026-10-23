using System.Net;
using System.Net.Sockets;
using System.Text;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// Connections that would otherwise stay open for ever: a client that never
/// finishes a request's headers (<see cref="InterceptingProxyListener.ClientIdleTimeout"/>),
/// and an upgraded connection one side of which has gone away.
/// </summary>
public class ListenerTimeoutTests
{
    private static readonly TimeSpan ShortIdle = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static InterceptingProxyListener StartListener(TimeSpan idleTimeout)
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1));
        var listener = new InterceptingProxyListener(0, new LeafCertificateProvider(root), new SessionStore())
        {
            ClientIdleTimeout = idleTimeout,
        };
        listener.Start();
        return listener;
    }

    /// <summary>Reads until the far end closes; fails the test if it doesn't within <see cref="TestTimeout"/>.</summary>
    private static async Task AssertClosedAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        var buffer = new byte[1024];
        try
        {
            while (await stream.ReadAsync(buffer, timeout.Token) > 0)
            {
            }
        }
        catch (IOException)
        {
            // Closed with a reset rather than a FIN: closed all the same.
        }
    }

    /// <summary>
    /// A one-connection origin server that runs <paramref name="serve"/>
    /// against whatever connects first.
    /// </summary>
    private static (int Port, Task Served) StartOrigin(Func<NetworkStream, Task> serve)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (port, ServeAsync());

        async Task ServeAsync()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                await serve(client.GetStream());
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    [Fact]
    public async Task ClosesAConnectionThatNeverSendsARequest()
    {
        var listener = StartListener(ShortIdle);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port);

            await AssertClosedAsync(client.GetStream());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosesAConnectionThatStallsHalfwayThroughItsHeaders()
    {
        var listener = StartListener(ShortIdle);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port);
            var stream = client.GetStream();
            await stream.WriteAsync("GET http://127.0.0.1/ HTTP/1.1\r\nHost: 127."u8.ToArray());

            await AssertClosedAsync(stream);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosesAConnectionThatSendsConnectButNoTlsHandshake()
    {
        var listener = StartListener(ShortIdle);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port);
            var stream = client.GetStream();
            await stream.WriteAsync("CONNECT example.com:443 HTTP/1.1\r\nHost: example.com:443\r\n\r\n"u8.ToArray());

            using var timeout = new CancellationTokenSource(TestTimeout);
            var established = await Http1MessageReader.ReadResponsePreambleAsync(stream, timeout.Token);
            Assert.Equal(200, established!.StatusCode);
            await AssertClosedAsync(stream);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosesAKeptAliveConnectionLeftIdleBetweenRequests()
    {
        await using var origin = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        var listener = StartListener(ShortIdle);
        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes($"GET http://127.0.0.1:{origin.Port}/ HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n"), timeout.Token);
            var response = await Http1MessageReader.ReadResponseAsync(stream, isResponseToHeadRequest: false, timeout.Token);
            Assert.Equal("hello", Encoding.ASCII.GetString(response!.Body));

            // Kept alive, but no second request comes.
            await AssertClosedAsync(stream);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DoesNotTimeOutABodyThatArrivesSlowly()
    {
        // The idle timeout covers each request's headers only: a request
        // body and a response body that each pause for several times the
        // timeout still get through whole.
        var pause = ShortIdle * 4;
        var (originPort, served) = StartOrigin(async server =>
        {
            await UpstreamProxyTests.FakeProxy.ReadHeadAsync(server);
            var body = new byte[5];
            await server.ReadExactlyAsync(body);
            Assert.Equal("hello", Encoding.ASCII.GetString(body));

            await server.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nslow "u8.ToArray());
            await Task.Delay(pause);
            await server.WriteAsync("reply"u8.ToArray());
        });
        var listener = StartListener(ShortIdle);
        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    $"POST http://127.0.0.1:{originPort}/ HTTP/1.1\r\nHost: 127.0.0.1:{originPort}\r\nContent-Length: 5\r\n\r\n"),
                timeout.Token);
            await Task.Delay(pause, timeout.Token);
            await stream.WriteAsync("hello"u8.ToArray(), timeout.Token);

            var response = await Http1MessageReader.ReadResponseAsync(stream, isResponseToHeadRequest: false, timeout.Token);
            Assert.Equal("slow reply", Encoding.ASCII.GetString(response!.Body));
            await served.WaitAsync(timeout.Token);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task NoIdleTimeoutWaitsForTheRequest()
    {
        var listener = StartListener(Timeout.InfiniteTimeSpan);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port);
            var stream = client.GetStream();

            var read = stream.ReadAsync(new byte[1]).AsTask();
            Assert.NotSame(read, await Task.WhenAny(read, Task.Delay(ShortIdle * 4)));

            // Still open: a request now is answered.
            await stream.WriteAsync("GET http://violens/ HTTP/1.1\r\nHost: violens\r\n\r\n"u8.ToArray());
            Assert.Equal(1, await read.WaitAsync(TestTimeout));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void RejectsAnIdleTimeoutThatIsntPositive()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var listener = new InterceptingProxyListener(
            0, new LeafCertificateProvider(CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1))), new SessionStore());

        Assert.Equal(InterceptingProxyListener.DefaultClientIdleTimeout, listener.ClientIdleTimeout);
        Assert.Throws<ArgumentOutOfRangeException>(() => listener.ClientIdleTimeout = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => listener.ClientIdleTimeout = TimeSpan.FromSeconds(-1));
    }

    [Fact]
    public async Task EndsAnUpgradedConnectionOnceTheServerHasClosed()
    {
        // A 101 that isn't a WebSocket is relayed as raw bytes. The server
        // closes its side straight after; the client never closes its own,
        // so the relay has to give up on it after WebSocketRelay.CloseGrace
        // rather than wait for the client for ever.
        await using var origin = UpstreamProxyTests.FakeProxy.Start(
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: example/1\r\nConnection: Upgrade\r\n\r\n");
        var listener = StartListener(Timeout.InfiniteTimeSpan);
        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(
                Encoding.ASCII.GetBytes(
                    $"GET http://127.0.0.1:{origin.Port}/ HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nUpgrade: example/1\r\nConnection: Upgrade\r\n\r\n"),
                timeout.Token);
            var switched = await Http1MessageReader.ReadResponsePreambleAsync(stream, timeout.Token);
            Assert.Equal(101, switched!.StatusCode);

            await AssertClosedAsync(stream);
        }
        finally
        {
            listener.Stop();
        }
    }
}
