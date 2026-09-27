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
    public async Task AnswersOtherPlainHttpRequestsInsteadOfDroppingThem()
    {
        using var root = NewRoot();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), new SessionStore());
        try
        {
            var response = await SendPlainAsync(listener.Port, "GET http://example.test/ HTTP/1.1\r\nHost: example.test\r\n\r\n");

            Assert.Equal(501, response.StatusCode);
            Assert.Contains("only captures HTTPS", Encoding.UTF8.GetString(response.Body));
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
