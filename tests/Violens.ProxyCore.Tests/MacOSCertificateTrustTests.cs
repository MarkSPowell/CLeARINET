using Violens.ProxyCore.Certificates;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>
/// Install and Uninstall run the real <c>security</c> command on the login
/// keychain, so only the argument builders are tested here.
/// </summary>
public class MacOSCertificateTrustTests
{
    [Fact]
    public void BuildAddTrustedCertArguments_TrustsTheGivenFileAsARootForEveryPolicy()
    {
        var arguments = MacOSCertificateTrust.BuildAddTrustedCertArguments("/tmp/violens-root-abc123.cer");

        Assert.Equal(["add-trusted-cert", "-r", "trustRoot", "/tmp/violens-root-abc123.cer"], arguments);
    }

    [Fact]
    public void BuildDeleteCertificateArguments_MatchesBySha1Thumbprint()
    {
        var arguments = MacOSCertificateTrust.BuildDeleteCertificateArguments("AABBCCDDEEFF00112233445566778899AABBCC");

        Assert.Equal(["delete-certificate", "-Z", "AABBCCDDEEFF00112233445566778899AABBCC"], arguments);
    }
}
