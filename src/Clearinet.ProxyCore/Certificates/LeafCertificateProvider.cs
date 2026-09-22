using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Clearinet.ProxyCore.Certificates;

/// <summary>
/// Signs a short-lived leaf certificate for each host CLeARINET intercepts,
/// caching results so a host is only re-signed once its cached certificate
/// is close to expiring. See the "Leaf certificate generation" section of
/// the Interception Certificate Design doc for the reasoning behind the
/// algorithm, SAN and validity choices here.
/// </summary>
public sealed class LeafCertificateProvider
{
    private static readonly TimeSpan LeafValidity = TimeSpan.FromDays(7);
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    private readonly X509Certificate2 _issuer;
    private readonly ConcurrentDictionary<string, X509Certificate2> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public LeafCertificateProvider(X509Certificate2 issuer)
    {
        if (!issuer.HasPrivateKey)
        {
            throw new ArgumentException(
                "The issuer certificate must carry its private key to sign leaf certificates.",
                nameof(issuer));
        }

        _issuer = issuer;
    }

    /// <summary>
    /// Returns a leaf certificate for <paramref name="host"/>, signing and
    /// caching a new one if there's no cached certificate with at least
    /// five minutes of validity left.
    /// </summary>
    public X509Certificate2 GetCertificateFor(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (_cache.TryGetValue(host, out var cached) && cached.NotAfter > DateTime.UtcNow.AddMinutes(5))
        {
            return cached;
        }

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.Add(LeafValidity);
        var leaf = CreateLeaf(_issuer, host, notBefore, notAfter);

        _cache[host] = leaf;
        return leaf;
    }

    /// <summary>
    /// Signs a single leaf certificate for <paramref name="host"/>. Kept
    /// separate from the cache so it can be unit tested directly against a
    /// root produced by <see cref="CertificateAuthority.GenerateRoot"/>,
    /// without touching any OS certificate store.
    /// </summary>
    public static X509Certificate2 CreateLeaf(
        X509Certificate2 issuer,
        string host,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        var subjectBuilder = new X500DistinguishedNameBuilder();
        subjectBuilder.AddCommonName(host);
        var subject = subjectBuilder.Build();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(host, out var address))
        {
            sanBuilder.AddIpAddress(address);
        }
        else
        {
            sanBuilder.AddDnsName(host);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        // ECDSA leaves only need DigitalSignature; KeyEncipherment is an
        // RSA-specific usage and doesn't apply to an EC key.
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(ServerAuthenticationOid) },
                critical: false));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Ties this leaf to the issuing root's key for chain building,
        // which strict validators (and some Chromium checks) expect even
        // for a two-level chain.
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                issuer,
                includeKeyIdentifier: true,
                includeIssuerAndSerial: false));

        var serialNumber = RandomNumberGenerator.GetBytes(16);
        using var signed = request.Create(issuer, notBefore, notAfter, serialNumber);
        return signed.CopyWithPrivateKey(key);
    }
}
