using Clearinet.ProxyCore.Certificates;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// MacOSCertificateTrust.Install/Uninstall shell out to the real
/// <c>security</c> CLI against the current user's actual login keychain --
/// exactly the same reason WinInetSystemProxyTests only exercises
/// WinInetSystemProxy.MergeBypassList and leaves the registry-touching
/// methods to manual verification. There's no macOS machine available to
/// this session to run the real commands against in the first place (see
/// the Interception Certificate Design doc's "Testing scope, deliberately
/// narrow" note), so this sticks to the same pure, no-subprocess shape:
/// only the argument-building methods, which are plain string constructors
/// with no OS interaction at all.
/// </summary>
public class MacOSCertificateTrustTests
{
    [Fact]
    public void BuildAddTrustedCertArguments_TrustsTheGivenFileAsARootForEveryPolicy()
    {
        var arguments = MacOSCertificateTrust.BuildAddTrustedCertArguments("/tmp/clearinet-root-abc123.cer");

        Assert.Equal(["add-trusted-cert", "-r", "trustRoot", "/tmp/clearinet-root-abc123.cer"], arguments);
    }

    [Fact]
    public void BuildDeleteCertificateArguments_MatchesBySha1Thumbprint()
    {
        var arguments = MacOSCertificateTrust.BuildDeleteCertificateArguments("AABBCCDDEEFF00112233445566778899AABBCC");

        Assert.Equal(["delete-certificate", "-Z", "AABBCCDDEEFF00112233445566778899AABBCC"], arguments);
    }
}
