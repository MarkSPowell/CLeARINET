using System.Net;
using System.Net.Sockets;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Proxy;
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
