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

    // Lazy<T>, not X509Certificate2, as the cached value: a real browser
    // opens several parallel/speculative connections to a host it hasn't
    // talked to yet, and those all hit GetCertificateFor for the same
    // uncached host within milliseconds of each other. A plain
    // check-then-create cache lets each of those race into CreateLeaf
    // independently -- signing several redundant certificates and, worse,
    // creating several redundant persisted CNG key containers (see
    // PersistKey below) for the exact same host. Under that kind of burst,
    // that was reproduced live hitting intermittent
    // "AuthenticationException ... Win32Exception: An unknown error
    // occurred while processing the certificate" failures -- plausibly
    // Windows' certificate/key-store machinery not being happy about
    // several near-simultaneous create-and-immediately-use operations.
    // ConcurrentDictionary<string, Lazy<T>> with
    // ExecutionAndPublication makes this single-flight instead: only the
    // first caller for a given uncached host actually runs CreateLeaf: the
    // callers that raced in behind it just wait for that one result.
    private readonly ConcurrentDictionary<string, Lazy<X509Certificate2>> _cache =
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
    /// five minutes of validity left. Concurrent calls for the same host
    /// share a single signing operation rather than each racing their own.
    /// </summary>
    public X509Certificate2 GetCertificateFor(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        while (true)
        {
            var lazyLeaf = _cache.GetOrAdd(host, CreateLazyLeaf);

            try
            {
                var certificate = lazyLeaf.Value;
                if (certificate.NotAfter > DateTime.UtcNow.AddMinutes(5))
                {
                    return certificate;
                }

                // Close to expiring: drop this entry and loop around to
                // regenerate it. If several callers see it expired at once
                // they'll all attempt this same remove -- harmless, since
                // at most one of them will actually win the next GetOrAdd's
                // factory call and the rest will just share that result.
                _cache.TryRemove(new KeyValuePair<string, Lazy<X509Certificate2>>(host, lazyLeaf));
            }
            catch
            {
                // Signing failed for this host. With
                // ExecutionAndPublication, a Lazy<T> caches and rethrows a
                // faulted evaluation forever -- so without this, every
                // future request for this host would keep failing with the
                // same stale error until the process restarts, even after
                // whatever caused it has passed. Drop the poisoned entry
                // so the next caller gets a genuinely fresh attempt.
                _cache.TryRemove(new KeyValuePair<string, Lazy<X509Certificate2>>(host, lazyLeaf));
                throw;
            }
        }
    }

    private Lazy<X509Certificate2> CreateLazyLeaf(string host) => new(
        () =>
        {
            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            var notAfter = notBefore.Add(LeafValidity);

            // A leaf can't be valid outside its issuer's validity (signing
            // throws). Only matters for a root made in the last few minutes,
            // or one near expiry, but then it matters for every leaf.
            var issuerNotBefore = new DateTimeOffset(_issuer.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var issuerNotAfter = new DateTimeOffset(_issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            if (notBefore < issuerNotBefore)
            {
                notBefore = issuerNotBefore;
            }

            if (notAfter > issuerNotAfter)
            {
                notAfter = issuerNotAfter;
            }

            return CreateLeaf(_issuer, host, notBefore, notAfter);
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

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
        using var signedWithEphemeralKey = signed.CopyWithPrivateKey(key);

        return PersistKey(signedWithEphemeralKey);
    }

    private static X509Certificate2 PersistKey(X509Certificate2 certWithEphemeralKey)
    {
        // On Windows, SslStream's server-side handshake (Schannel) refuses
        // a certificate whose private key exists only in memory --
        // CopyWithPrivateKey's result -- and throws "Authentication
        // failed because the platform does not support ephemeral keys."
        // Exporting to a PFX and re-importing with PersistKeySet gives it
        // a real (if short-lived) CNG key container instead, which
        // Schannel accepts. Same pattern as the root's own key handling,
        // just without also adding the certificate to a visible store --
        // only the key needs to stop being ephemeral.
        //
        // Trade-off worth tracking: this leaves a small key-container
        // artifact behind per leaf generated. Cleaning those up as leaves
        // expire and are evicted from the cache is a real follow-up, not
        // something this spike solves.
        //
        // A freshly persisted key container isn't always immediately ready
        // for Schannel to grab -- reproduced live as an occasional,
        // isolated (not just under concurrent-signing bursts, which
        // GetCertificateFor's single-flight cache already guards against)
        // "AuthenticationException ... Win32Exception: An unknown error
        // occurred while processing the certificate" failure, right in the
        // middle of a real client's TLS handshake. WarmUpPrivateKey forces
        // one real private-key operation here instead, before this
        // certificate is ever handed to SslStream, so that class of
        // failure surfaces (and can be retried) right after import, in a
        // spot where it's cheap to reason about -- not mid-handshake,
        // where it silently drops one of the client's connections and
        // costs us a capture.
        const int maxAttempts = 2;
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var password = Guid.NewGuid().ToString("N");
            var pfxBytes = certWithEphemeralKey.Export(X509ContentType.Pfx, password);
            X509Certificate2? persisted = null;

            try
            {
                persisted = X509CertificateLoader.LoadPkcs12(
                    pfxBytes,
                    password,
                    X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

                WarmUpPrivateKey(persisted);
                return persisted;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                persisted?.Dispose();
            }
            finally
            {
                Array.Clear(pfxBytes);
            }
        }

        throw new CryptographicException(
            $"Failed to obtain a usable persisted private key after {maxAttempts} attempts.", lastFailure);
    }

    private static void WarmUpPrivateKey(X509Certificate2 certificate)
    {
        using var ecdsa = certificate.GetECDsaPrivateKey()
            ?? throw new CryptographicException("Persisted leaf certificate has no usable ECDSA private key.");
        _ = ecdsa.SignData("clearinet-key-warmup"u8.ToArray(), HashAlgorithmName.SHA256);
    }
}
