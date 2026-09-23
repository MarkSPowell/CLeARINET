using System.Net;
using System.Net.Sockets;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

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
        // CLeARINET process (or a previous run of this one) is still
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
        // Ask the OS for a free port, then let it go immediately -- the
        // same "port 0" trick StartOnAvailablePort itself uses for its
        // fallback. There's an inherent, accepted race between releasing
        // it here and the test binding it again a moment later, but that's
        // the standard way to get an assuredly-free ephemeral port to test
        // against without hardcoding one that might collide on a shared
        // CI machine.
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static LeafCertificateProvider CreateLeafProvider()
    {
        // Deliberately not disposed: LeafCertificateProvider just holds
        // this reference, and disposing it here (the way single-test
        // helpers elsewhere do with a local `using`) would leave that
        // reference pointing at a disposed certificate for the rest of
        // this provider's lifetime -- harmless for these tests, which
        // never sign a leaf, but a landmine for whoever extends them next.
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddYears(5));
        return new LeafCertificateProvider(root);
    }
}
