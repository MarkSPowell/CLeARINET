using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Clearinet.ProxyCore.Certificates;

/// <summary>
/// Owns the per-install, per-user CLeARINET root certificate authority:
/// generating it, persisting its private key with a non-exportable storage
/// flag, and installing it into the current user's trusted-root store. See
/// the "CLeARINET Interception Certificate Design" doc for the reasoning
/// behind these choices; this class is a first implementation of the
/// Windows half of that design, written to be validated against a real
/// browser before the macOS path is tackled.
/// </summary>
public sealed class CertificateAuthority
{
    /// <summary>
    /// Common-name prefix every CLeARINET root uses, so a stale or foreign
    /// copy is recognizable in a trust store at a glance. Named after
    /// Fiddler Classic's own long-standing convention
    /// (<c>DO_NOT_TRUST_FiddlerRoot</c>).
    /// </summary>
    public const string SubjectPrefix = "DO_NOT_TRUST_ClearinetRoot";

    private static readonly TimeSpan RootValidity = TimeSpan.FromDays(365 * 5);

    public X509Certificate2 RootCertificate { get; }

    /// <param name="installToTrustStore">
    /// When true (the default), ensures the root is present in the current
    /// user's trusted-root store, which is what triggers the native
    /// Windows "Do you want to install this certificate?" prompt the first
    /// time. Tests and tooling that only need the certificate object
    /// itself should pass false to avoid touching the trust store.
    /// </param>
    public CertificateAuthority(bool installToTrustStore = true)
    {
        RootCertificate = LoadOrCreateRoot();

        if (installToTrustStore)
        {
            EnsureTrusted(RootCertificate);
        }
    }

    /// <summary>
    /// Generates a fresh, self-signed CLeARINET root certificate. This is
    /// pure certificate math with no store or OS interaction, which is why
    /// it's the part of this class that's actually unit-testable.
    /// </summary>
    public static X509Certificate2 GenerateRoot(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        // Built with X500DistinguishedNameBuilder rather than the
        // string-parsing X500DistinguishedName constructor: the common
        // name below contains a comma, which the string form treats as an
        // RDN separator unless escaped. The builder adds it as a single
        // attribute value, no escaping needed.
        var subjectBuilder = new X500DistinguishedNameBuilder();
        subjectBuilder.AddCommonName(
            $"{SubjectPrefix} ({Environment.MachineName}, generated {DateTime.UtcNow:yyyy-MM-dd})");
        var subject = subjectBuilder.Build();

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 LoadOrCreateRoot()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Find(X509FindType.FindBySubjectName, SubjectPrefix, validOnly: false)
            .Cast<X509Certificate2>()
            .Where(c => c.NotAfter > DateTime.UtcNow && c.HasPrivateKey)
            .OrderByDescending(c => c.NotBefore)
            .FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.Add(RootValidity);

        using var generated = GenerateRoot(notBefore, notAfter);
        return PersistNonExportable(store, generated);
    }

    private static X509Certificate2 PersistNonExportable(X509Store store, X509Certificate2 certWithKey)
    {
        // .NET only lets you choose the storage flags (PersistKeySet,
        // deliberately without Exportable) on *import*, not on the
        // certificate CreateSelfSigned just handed back. So: export to a
        // PFX in memory, then immediately re-import with the flags we
        // actually want. The password only protects the bytes for the
        // instant they exist inside this method.
        var password = Guid.NewGuid().ToString("N");
        var pfxBytes = certWithKey.Export(X509ContentType.Pfx, password);

        try
        {
            var persisted = X509CertificateLoader.LoadPkcs12(
                pfxBytes,
                password,
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

            store.Add(persisted);
            return persisted;
        }
        finally
        {
            Array.Clear(pfxBytes);
        }
    }

    private static void EnsureTrusted(X509Certificate2 rootCert)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        var alreadyTrusted = store.Certificates
            .Find(X509FindType.FindByThumbprint, rootCert.Thumbprint, validOnly: false)
            .Count > 0;

        if (!alreadyTrusted)
        {
            // On Windows this is what triggers the native "Do you want to
            // install this certificate?" dialog -- the same prompt
            // Fiddler Classic's own docs walk users through. Installation
            // should never be silent; this relies on that OS behavior
            // rather than trying to suppress or replace it.
            store.Add(rootCert);
        }
    }

    /// <summary>
    /// Removes this root from both the personal and trusted-root stores.
    /// Implements the design doc's "one-click removal" mechanics; not yet
    /// wired to any UI or CLI command.
    /// </summary>
    public void Uninstall()
    {
        using (var rootStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
        {
            rootStore.Open(OpenFlags.ReadWrite);
            rootStore.Remove(RootCertificate);
        }

        using (var myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            myStore.Open(OpenFlags.ReadWrite);
            myStore.Remove(RootCertificate);
        }
    }
}
