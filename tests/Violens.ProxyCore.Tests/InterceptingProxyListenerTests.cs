using System.Net;
using System.Net.Sockets;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Proxy.AutoConfig;
using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

public class InterceptingProxyListenerTests
{
    [Fact]
    public void StartOnAvailablePort_UsesThePreferredPortWhenItIsFree()
    {
        var freePort = ReserveAndReleaseAFreePort();
        var proxy = InterceptingProxyListener.StartOnAvailablePort(freePort, CreateLeafProvider(), new SessionStore());

        try
        {
            Assert.Equal(freePort, proxy.Port);
        }
        finally
        {
            proxy.Stop();
        }
    }

    [Fact]
    public void StartOnAvailablePort_FallsBackToAFreePortWhenThePreferredOneIsTaken()
    {
        // Holds the port open for the whole test, standing in for "another
        // VioLens process (or a previous run of this one) is still
        // listening here" -- the actual failure mode reported live against
        // the desktop app defaulting to a fixed 8888.
        using var blocker = new TcpListener(IPAddress.Loopback, 0);
        blocker.Start();
        var takenPort = ((IPEndPoint)blocker.LocalEndpoint).Port;

        var proxy = InterceptingProxyListener.StartOnAvailablePort(takenPort, CreateLeafProvider(), new SessionStore());

        try
        {
            Assert.NotEqual(takenPort, proxy.Port);
            Assert.True(proxy.Port > 0);
        }
        finally
        {
            proxy.Stop();
        }
    }

    [Theory]
    [InlineData("example.com:443", "example.com", 443)]
    [InlineData("example.com:8443", "example.com", 8443)]
    [InlineData("example.com", "example.com", 443)]
    [InlineData("example.com:nope", "example.com", 443)]
    [InlineData("example.com:70000", "example.com", 443)]
    [InlineData("127.0.0.1:8443", "127.0.0.1", 8443)]
    [InlineData("[::1]:443", "::1", 443)]
    [InlineData("[2001:db8::7]:8443", "2001:db8::7", 8443)]
    [InlineData("[2001:db8::7]", "2001:db8::7", 443)]
    [InlineData("2001:db8::7", "2001:db8::7", 443)]
    [InlineData("::1", "::1", 443)]
    public void ParseConnectTarget_ReadsHostAndPortIncludingIPv6(string target, string host, int port)
    {
        Assert.Equal((host, port), InterceptingProxyListener.ParseConnectTarget(target));
    }

    [Fact]
    public async Task UpstreamWebProxy_ReusesAPacAnswerPerHost()
    {
        // The script answers with a different proxy every time it runs, so
        // which one comes back shows whether it ran again.
        var listener = new InterceptingProxyListener(0, CreateLeafProvider(), new SessionStore());
        using var autoConfig = ProxyAutoConfig.FromScript(
            "var n = 0; function FindProxyForURL(url, host) { n++; return 'PROXY p' + n + '.example:8080'; }",
            new PacScriptTests.StubPacHost());
        listener.AutoConfig = autoConfig;
        var proxy = new InterceptingProxyListener.UpstreamWebProxy(listener);

        // Prepared ahead, without blocking: the synchronous questions after
        // it are answered from the cache, for any path on that host.
        await proxy.PrepareRouteAsync(new Uri("http://a.example/one"), CancellationToken.None);
        Assert.Equal(new Uri("http://p1.example:8080/"), proxy.GetProxy(new Uri("http://a.example/one")));
        Assert.False(proxy.IsBypassed(new Uri("http://a.example/two")));
        Assert.Equal(new Uri("http://p1.example:8080/"), proxy.GetProxy(new Uri("http://a.example/two")));

        // Another host, or another scheme on the same one, is asked about afresh.
        Assert.Equal(new Uri("http://p2.example:8080/"), proxy.GetProxy(new Uri("http://b.example/")));
        Assert.Equal(new Uri("http://p3.example:8080/"), proxy.GetProxy(new Uri("https://a.example/")));

        // A replaced script's answers aren't reused.
        using var replacement = ProxyAutoConfig.FromScript(
            "function FindProxyForURL(url, host) { return 'DIRECT'; }", new PacScriptTests.StubPacHost());
        listener.AutoConfig = replacement;
        Assert.True(proxy.IsBypassed(new Uri("http://a.example/one")));
    }

    [Fact]
    public void UpstreamWebProxy_UsesTheGatewayAndLoopbackWithoutTheScript()
    {
        var listener = new InterceptingProxyListener(0, CreateLeafProvider(), new SessionStore());
        using var autoConfig = ProxyAutoConfig.FromScript(
            "function FindProxyForURL(url, host) { return 'PROXY pac.example:8080'; }", new PacScriptTests.StubPacHost());
        listener.AutoConfig = autoConfig;
        var proxy = new InterceptingProxyListener.UpstreamWebProxy(listener);

        Assert.True(proxy.IsBypassed(new Uri("http://127.0.0.1:9000/")));

        listener.Gateway = new UpstreamProxy("gateway.example", 3128, []);
        Assert.Equal(new Uri("http://gateway.example:3128/"), proxy.GetProxy(new Uri("http://a.example/")));
    }

    private static int ReserveAndReleaseAFreePort()
    {
        // Get a free port from the OS and release it. Something could take it
        // before the test binds it, but that's the usual trade-off.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static LeafCertificateProvider CreateLeafProvider()
    {
        // Not disposed: the provider keeps using it.
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddYears(5));
        return new LeafCertificateProvider(root);
    }
}
