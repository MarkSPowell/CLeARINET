using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Violens.ProxyCore.Certificates;
using Xunit;

namespace Violens.ProxyCore.Tests;

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

    [Fact]
    public void RemoveRootsFromStores_RemovesMatchingRootsAndTheirKeysOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Stores of the test's own, never the real trusted-root store (whose
        // removals show a system prompt).
        var trustName = $"ViolensTestTrust-{Guid.NewGuid():N}";
        var keyName = $"ViolensTestKeys-{Guid.NewGuid():N}";
        var prefix = $"DO_NOT_TRUST_TestRoot{Guid.NewGuid():N}";
        try
        {
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            using var generated = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1), prefix);
            using var root = Persist(generated);
            using var other = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1), "DO_NOT_TRUST_SomeoneElse");
            string rootKeyName;
            CngProvider rootKeyProvider;
            using (var key = (ECDsaCng)root.GetECDsaPrivateKey()!)
            {
                rootKeyName = key.Key.KeyName!;
                rootKeyProvider = key.Key.Provider!;
            }

            using (var trust = new X509Store(trustName, StoreLocation.CurrentUser))
            using (var keys = new X509Store(keyName, StoreLocation.CurrentUser))
            {
                trust.Open(OpenFlags.ReadWrite);
                keys.Open(OpenFlags.ReadWrite);
                using var publicOnly = X509CertificateLoader.LoadCertificate(root.Export(X509ContentType.Cert));
                trust.Add(publicOnly);
                trust.Add(other);
                keys.Add(root);
            }

            using (var trust = new X509Store(trustName, StoreLocation.CurrentUser))
            using (var keys = new X509Store(keyName, StoreLocation.CurrentUser))
            {
                var removed = CertificateAuthority.RemoveRootsFromStores(trust, keys, prefix, log: null);

                Assert.Equal(1, removed);
                Assert.Empty(trust.Certificates.Find(X509FindType.FindBySubjectName, prefix, validOnly: false));
                Assert.Empty(keys.Certificates.Find(X509FindType.FindBySubjectName, prefix, validOnly: false));
                Assert.Single(trust.Certificates.Find(X509FindType.FindByThumbprint, other.Thumbprint, validOnly: false));
            }

            Assert.False(CngKey.Exists(rootKeyName, rootKeyProvider, CngKeyOpenOptions.UserKey));
        }
        finally
        {
            foreach (var name in new[] { trustName, keyName })
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\SystemCertificates\{name}", throwOnMissingSubKey: false);
            }
        }
    }

    // As CertificateAuthority does for the real root: a persisted key.
    private static X509Certificate2 Persist(X509Certificate2 certificate)
    {
        var pfx = certificate.Export(X509ContentType.Pfx, "test");
        return X509CertificateLoader.LoadPkcs12(pfx, "test", X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);
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
