using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clearinet.ProxyCore.Certificates;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class LeafCertificateProviderTests
{
    [Fact]
    public void CreateLeaf_IsSignedByTheIssuerAndCarriesTheHostAsASubjectAlternativeName()
    {
        var rootNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(rootNotBefore, rootNotBefore.AddYears(5));

        var leafNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var leaf = LeafCertificateProvider.CreateLeaf(
            root, "example.test", leafNotBefore, leafNotBefore.AddDays(7));

        Assert.True(leaf.HasPrivateKey);
        Assert.Equal(root.Subject, leaf.Issuer);
        Assert.Equal("CN=example.test", leaf.Subject);

        var san = GetSubjectAlternativeNames(leaf);
        Assert.Contains("example.test", san.EnumerateDnsNames());

        var eku = GetEnhancedKeyUsage(leaf);
        Assert.Contains(eku.EnhancedKeyUsages.Cast<Oid>(), oid => oid.Value == "1.3.6.1.5.5.7.3.1");
    }

    [Fact]
    public void CreateLeaf_UsesAnIpAddressSanForAnIpHost()
    {
        var rootNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(rootNotBefore, rootNotBefore.AddYears(5));

        var leafNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var leaf = LeafCertificateProvider.CreateLeaf(
            root, "127.0.0.1", leafNotBefore, leafNotBefore.AddDays(7));

        var san = GetSubjectAlternativeNames(leaf);
        Assert.Contains(san.EnumerateIPAddresses(), ip => ip.ToString() == "127.0.0.1");
    }

    [Fact]
    public void CreateLeaf_ReturnedCertificateHasAnImmediatelyUsablePrivateKey()
    {
        // Regression guard for PersistKey's warm-up step: the certificate
        // this returns must actually be able to sign with its private key
        // right away, not just report HasPrivateKey == true.
        var rootNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(rootNotBefore, rootNotBefore.AddYears(5));

        var leafNotBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var leaf = LeafCertificateProvider.CreateLeaf(
            root, "example.test", leafNotBefore, leafNotBefore.AddDays(7));

        using var ecdsa = leaf.GetECDsaPrivateKey();
        Assert.NotNull(ecdsa);
        var signature = ecdsa!.SignData("probe"u8.ToArray(), HashAlgorithmName.SHA256);
        Assert.NotEmpty(signature);
    }

    [Fact]
    public async Task GetCertificateFor_ConcurrentCallsForANewHostShareOneSigningOperation()
    {
        // Simulates what a browser's burst of parallel preconnects to a
        // brand-new host looks like: many callers racing GetCertificateFor
        // before anything is cached yet. CreateLeaf generates a fresh key
        // and a fresh CNG-backed certificate every time it runs, so if the
        // race isn't handled, this comes back with several distinct
        // certificate instances instead of everyone sharing one -- which is
        // exactly the bug behind the intermittent "unknown error processing
        // the certificate" failures reproduced against real, bursty browser
        // traffic.
        var rootNotBefore = DateTimeOffset.UtcNow.AddDays(-1);
        using var root = CertificateAuthority.GenerateRoot(rootNotBefore, rootNotBefore.AddYears(5));
        var provider = new LeafCertificateProvider(root);

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => provider.GetCertificateFor("example.test")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, cert => Assert.Same(results[0], cert));
    }

    [Fact]
    public void GetCertificateFor_CachesByHost()
    {
        // GetCertificateFor backdates its leaf's notBefore by 5 minutes
        // for clock-skew tolerance, so the root here needs a wider margin
        // than the other tests' -1 minute -- otherwise the leaf's
        // notBefore can land earlier than the issuer's own notBefore,
        // exactly like production's real root (backdated a full day) is
        // never at risk of.
        var rootNotBefore = DateTimeOffset.UtcNow.AddDays(-1);
        using var root = CertificateAuthority.GenerateRoot(rootNotBefore, rootNotBefore.AddYears(5));

        var provider = new LeafCertificateProvider(root);
        var first = provider.GetCertificateFor("example.test");
        var second = provider.GetCertificateFor("example.test");

        Assert.Same(first, second);
    }

    private static X509SubjectAlternativeNameExtension GetSubjectAlternativeNames(X509Certificate2 cert)
    {
        var raw = cert.Extensions["2.5.29.17"]
            ?? throw new InvalidOperationException("Subject Alternative Name extension is missing.");
        return new X509SubjectAlternativeNameExtension(raw.RawData, raw.Critical);
    }

    private static X509EnhancedKeyUsageExtension GetEnhancedKeyUsage(X509Certificate2 cert)
    {
        var raw = cert.Extensions["2.5.29.37"]
            ?? throw new InvalidOperationException("Enhanced Key Usage extension is missing.");
        return new X509EnhancedKeyUsageExtension(raw, raw.Critical);
    }
}
