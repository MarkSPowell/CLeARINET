using System.Security.Cryptography.X509Certificates;
using Clearinet.ProxyCore.Certificates;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class CertificateAuthorityTests
{
    [Fact]
    public void GenerateRoot_ProducesASelfSignedCertificateAuthority()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var notAfter = notBefore.AddYears(5);

        using var root = CertificateAuthority.GenerateRoot(notBefore, notAfter);

        Assert.True(root.HasPrivateKey);
        // Not StartsWith("CN=..."): the common name contains a comma, so
        // the formatter quotes the whole value (CN="..."). Containment is
        // what we actually care about here, not the escaping style.
        Assert.Contains(CertificateAuthority.SubjectPrefix, root.Subject, StringComparison.Ordinal);
        Assert.Equal(root.Subject, root.Issuer); // self-signed

        var basicConstraints = GetBasicConstraints(root);
        Assert.True(basicConstraints.CertificateAuthority);
        Assert.True(basicConstraints.Critical);
        Assert.True(basicConstraints.HasPathLengthConstraint);
        Assert.Equal(0, basicConstraints.PathLengthConstraint);

        var keyUsage = GetKeyUsage(root);
        Assert.True(keyUsage.Critical);
        Assert.True(keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign));
        Assert.True(keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.CrlSign));
    }

    [Fact]
    public void GenerateRoot_CannotBeUsedToSignFurtherSubCertificateAuthorities()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddYears(5));

        var basicConstraints = GetBasicConstraints(root);

        // pathLengthConstraint = 0 means this CA can sign leaves, but not
        // any further intermediate/sub CAs.
        Assert.Equal(0, basicConstraints.PathLengthConstraint);
    }

    private static X509BasicConstraintsExtension GetBasicConstraints(X509Certificate2 cert)
    {
        var raw = cert.Extensions["2.5.29.19"]
            ?? throw new InvalidOperationException("Basic Constraints extension is missing.");
        return new X509BasicConstraintsExtension(raw, raw.Critical);
    }

    private static X509KeyUsageExtension GetKeyUsage(X509Certificate2 cert)
    {
        var raw = cert.Extensions["2.5.29.15"]
            ?? throw new InvalidOperationException("Key Usage extension is missing.");
        return new X509KeyUsageExtension(raw, raw.Critical);
    }
}
