using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Clearinet.ProxyCore.Certificates;

/// <summary>
/// Owns the per-install, per-user CLeARINET root certificate authority:
/// generating it, persisting its private key with a non-exportable storage
/// flag, and installing it into the current user's trusted-root store. See
/// the "CLeARINET Interception Certificate Design" doc for the reasoning
/// behind these choices, including "Platform status" for the macOS half
/// (<see cref="MacOSCertificateTrust"/>) built alongside the original,
/// validated-on-a-real-browser Windows half below.
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

    /// <summary>
    /// Platform dispatch lives here, not spread across call sites -- see
    /// the design doc's "Platform status" section for the reasoning behind
    /// each platform's own approach. Previously unguarded entirely (always
    /// ran the Windows-only <c>X509Store(StoreName.Root, ...)</c> path
    /// regardless of platform) -- a latent bug on anything but Windows,
    /// since that call throws an unhelpful native
    /// <see cref="CryptographicException"/> there rather than a clear
    /// "unsupported platform" message. Fixed here alongside adding the
    /// macOS path itself.
    /// </summary>
    private static void EnsureTrusted(X509Certificate2 rootCert)
    {
        if (OperatingSystem.IsWindows())
        {
            EnsureTrustedWindows(rootCert);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // No "already trusted" short-circuit here the way Windows has
            // one -- MacOSCertificateTrust.Install always runs
            // `security add-trusted-cert`, which is itself already
            // idempotent (re-adding an already-trusted cert is a no-op,
            // not an error), so there's no correctness reason to duplicate
            // that check on this side too.
            MacOSCertificateTrust.Install(rootCert);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "CLeARINET's certificate trust-store installation is only implemented for Windows and macOS " +
                "-- see the Interception Certificate Design doc's 'Platform status' section.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void EnsureTrustedWindows(X509Certificate2 rootCert)
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
            // rather than trying to suppress or replace it. macOS has no
            // equivalent OS-level prompt for the shelled-out `security`
            // command this project's own MacOSCertificateTrust uses
            // instead -- see the design doc's "silent-install tension"
            // section for how that's resolved on that side (CLeARINET's
            // own confirmation dialog, not an OS one).
            store.Add(rootCert);
        }
    }

    /// <summary>
    /// Removes this root from both the personal and trusted-root stores.
    /// Implements the design doc's "one-click removal" mechanics; not yet
    /// wired to any UI or CLI command, on either platform.
    /// </summary>
    public void Uninstall()
    {
        if (OperatingSystem.IsWindows())
        {
            using var rootStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            rootStore.Open(OpenFlags.ReadWrite);
            rootStore.Remove(RootCertificate);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSCertificateTrust.Uninstall(RootCertificate);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "CLeARINET's certificate trust-store removal is only implemented for Windows and macOS " +
                "-- see the Interception Certificate Design doc's 'Platform status' section.");
        }

        // The personal ("My") store, unlike the trust store above, is
        // already cross-platform in .NET's own X509Store implementation
        // (it's how the macOS half of LoadOrCreateRoot's own store access
        // already works too, with no platform split needed there either)
        // -- no per-platform dispatch needed for this half.
        using var myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        myStore.Open(OpenFlags.ReadWrite);
        myStore.Remove(RootCertificate);
    }
}
