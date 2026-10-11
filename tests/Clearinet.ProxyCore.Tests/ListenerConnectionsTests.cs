using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// The listener's connection options: forwarding through an upstream proxy,
/// its own page and certificate for other devices, and listening for remote
/// computers.
/// </summary>
public class ListenerConnectionsTests
{
    private static X509Certificate2 NewRoot()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        return CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1));
    }

    [Fact]
    public async Task ServesItsHomePageAndCertificateOverPlainHttp()
    {
        using var root = NewRoot();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        try
        {
            var page = await SendPlainAsync(listener.Port, "GET http://clearinet/ HTTP/1.1\r\nHost: clearinet\r\n\r\n");
            Assert.Equal(200, page.StatusCode);
            Assert.Contains("text/html", ContentDecoder.FindHeader(page.Headers, "Content-Type"));
            Assert.Contains("ClearinetRoot.cer", Encoding.UTF8.GetString(page.Body));

            var certificate = await SendPlainAsync(listener.Port, "GET /ClearinetRoot.cer HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
            Assert.Equal(200, certificate.StatusCode);
            Assert.Equal(root.Export(X509ContentType.Cert), certificate.Body);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ForwardsPlainHttpAndRecordsIt()
    {
        using var root = NewRoot();
        await using var origin = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello");
        var (store, recorded) = RecordingStore();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), store);
        try
        {
            var response = await SendPlainAsync(
                listener.Port,
                $"GET http://127.0.0.1:{origin.Port}/hello?x=1 HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\nProxy-Connection: keep-alive\r\n\r\n");

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("hello", Encoding.ASCII.GetString(response.Body));

            // The server gets an ordinary request: path only, no proxy headers.
            var head = await origin.Received.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.StartsWith("GET /hello?x=1 HTTP/1.1\r\n", head);
            Assert.DoesNotContain("Proxy-Connection", head);

            var session = await recorded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("http", session.Scheme);
            Assert.Equal("127.0.0.1", session.Host);
            Assert.Equal("/hello?x=1", session.Request.Target);
            Assert.Equal($"127.0.0.1:{origin.Port}", ContentDecoder.FindHeader(session.Request.Headers, "Host"));
            Assert.Equal($"http://127.0.0.1:{origin.Port}/hello?x=1", SessionUrl.Of(session));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task OneClientConnectionCanReachDifferentServers()
    {
        using var root = NewRoot();
        await using var first = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nfirst");
        await using var second = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Length: 6\r\n\r\nsecond");
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();

            foreach (var (server, expected) in new[] { (first, "first"), (second, "second") })
            {
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes($"GET http://127.0.0.1:{server.Port}/ HTTP/1.1\r\nHost: 127.0.0.1:{server.Port}\r\n\r\n"), timeout.Token);
                var response = await Http1MessageReader.ReadResponseAsync(stream, isResponseToHeadRequest: false, timeout.Token);
                Assert.Equal(expected, Encoding.ASCII.GetString(response!.Body));
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task RelaysAResponseThatEndsWhenTheServerCloses()
    {
        using var root = NewRoot();
        // No Content-Length and not chunked: the body runs until the server
        // closes the connection (the fake server closes shortly after replying).
        await using var origin = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n\r\nuntil close");
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        try
        {
            var response = await SendPlainAsync(listener.Port, $"GET http://127.0.0.1:{origin.Port}/ HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n");

            Assert.Equal("until close", Encoding.ASCII.GetString(response.Body));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task AnswersBadGatewayWhenTheServerCantBeReached()
    {
        using var root = NewRoot();
        var (store, recorded) = RecordingStore();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), store);
        try
        {
            // Nothing listens on port 1.
            var response = await SendPlainAsync(listener.Port, "GET http://127.0.0.1:1/ HTTP/1.1\r\nHost: 127.0.0.1:1\r\n\r\n");

            Assert.Equal(502, response.StatusCode);
            Assert.Contains("couldn't connect to 127.0.0.1:1", Encoding.UTF8.GetString(response.Body));

            var session = await recorded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(502, session.Response.StatusCode);
            Assert.Equal("http", session.Scheme);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task AnswersRequestsItCantForwardInsteadOfDroppingThem()
    {
        using var root = NewRoot();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        try
        {
            var response = await SendPlainAsync(listener.Port, "GET ftp://example.test/file HTTP/1.1\r\nHost: example.test\r\n\r\n");

            Assert.Equal(501, response.StatusCode);
            Assert.Contains("forwards http:// and https:// requests only", Encoding.UTF8.GetString(response.Body));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendsPlainHttpThroughTheGatewayAsAProxyRequest()
    {
        using var root = NewRoot();
        await using var gateway = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        listener.Gateway = new UpstreamProxy("127.0.0.1", gateway.Port);
        try
        {
            var response = await SendPlainAsync(listener.Port, "GET http://example.test/a?b=1 HTTP/1.1\r\nHost: example.test\r\n\r\n");

            Assert.Equal("ok", Encoding.ASCII.GetString(response.Body));
            var head = await gateway.Received.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.StartsWith("GET http://example.test/a?b=1 HTTP/1.1\r\n", head);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ForwardsThroughTheGateway()
    {
        using var root = NewRoot();
        await using var gateway = UpstreamProxyTests.FakeProxy.Start("HTTP/1.1 502 Bad Gateway\r\n\r\n");
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        listener.Gateway = new UpstreamProxy("127.0.0.1", gateway.Port);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes("CONNECT example.test:443 HTTP/1.1\r\nHost: example.test:443\r\n\r\n"), timeout.Token);
            Assert.StartsWith("HTTP/1.1 200", await UpstreamProxyTests.FakeProxy.ReadHeadAsync(stream));

            using var tls = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync("example.test");
            await tls.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: example.test\r\n\r\n"), timeout.Token);

            // The listener opens its server connection through the gateway.
            var head = await gateway.Received.Task.WaitAsync(timeout.Token);
            Assert.StartsWith("CONNECT example.test:443 HTTP/1.1\r\n", head);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void CanListenForRemoteComputers()
    {
        using var root = NewRoot();
        var local = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        var remote = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore(), allowRemoteClients: true);
        try
        {
            Assert.False(local.AllowsRemoteClients);
            Assert.True(remote.AllowsRemoteClients);
        }
        finally
        {
            local.Stop();
            remote.Stop();
        }
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/ClearinetRoot.cer", true)]
    [InlineData("http://clearinet/", true)]
    [InlineData("http://CLEARINET/ClearinetRoot.cer", true)]
    [InlineData("http://127.0.0.1:8888/", true)]
    [InlineData("http://127.0.0.1:9999/", false)]
    [InlineData("http://example.test/", false)]
    public void RecognisesRequestsForItself(string target, bool forProxy)
    {
        Assert.Equal(forProxy, ProxyHomePage.IsRequestForProxy(target, 8888));
    }

    private static (SessionStore Store, TaskCompletionSource<Session> Recorded) RecordingStore()
    {
        var store = new SessionStore();
        var recorded = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.SessionAdded += session => recorded.TrySetResult(session);
        return (store, recorded);
    }

    private static async Task<CapturedResponse> SendPlainAsync(int port, string request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);
        var response = await Http1MessageReader.ReadResponseAsync(stream, isResponseToHeadRequest: false, timeout.Token);
        Assert.NotNull(response);
        return response!;
    }
}
