using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Violens.ProxyCore.Certificates;

namespace Violens.AppCore.Migration;

/// <summary>
/// Removes the root certificates VioLens made under its former name,
/// CLeARINET, so a renamed install doesn't leave a trusted root behind that
/// nothing uses (or can remove).
///
/// Call it only once the new root is trusted (so a declined prompt for the
/// new one never leaves the user with neither), and only when
/// <see cref="FormerNameMigration.Result.FoundFormerInstall"/> says this
/// computer had CLeARINET: another project with a similar name could, in
/// principle, use the same certificate name.
/// </summary>
public static class FormerRoots
{
    /// <summary>The common-name prefix CLeARINET's roots used.</summary>
    public const string SubjectPrefix = "DO_NOT_TRUST_ClearinetRoot";

    /// <summary>
    /// Removes any CLeARINET root from the trusted-root store and the
    /// personal store. On Windows each removal from the trusted-root store
    /// shows the system's own confirmation; a "No" is respected, logged, and
    /// asked again next time. Never throws.
    /// </summary>
    /// <returns>How many old roots were removed from the trust store.</returns>
    public static int Remove(Action<string>? log = null)
    {
        var removed = 0;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var rootStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                rootStore.Open(OpenFlags.ReadWrite);
                foreach (var old in Find(rootStore))
                {
                    try
                    {
                        rootStore.Remove(old);
                        removed++;
                    }
                    catch (CryptographicException ex)
                    {
                        log?.Invoke($"The old CLeARINET root ({old.Thumbprint}) is still trusted: {ex.Message}");
                    }
                }
            }

            using var myStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            myStore.Open(OpenFlags.ReadWrite);
            foreach (var old in Find(myStore))
            {
                try
                {
                    if (OperatingSystem.IsMacOS())
                    {
                        // Deleting it from the keychain takes its trust
                        // setting with it.
                        MacOSCertificateTrust.Uninstall(old);
                        removed++;
                    }
                    else if (OperatingSystem.IsWindows())
                    {
                        // Still trusted means the user said No above; keep
                        // the key so a later "Yes" leaves nothing behind.
                        using var rootStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                        rootStore.Open(OpenFlags.ReadOnly);
                        if (rootStore.Certificates.Find(X509FindType.FindByThumbprint, old.Thumbprint, validOnly: false).Count > 0)
                        {
                            continue;
                        }

                        myStore.Remove(old);
                    }
                }
                catch (Exception ex) when (ex is CryptographicException or CertificateTrustException)
                {
                    log?.Invoke($"Couldn't remove the old CLeARINET root ({old.Thumbprint}): {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            log?.Invoke($"Couldn't check for old CLeARINET roots: {ex.Message}");
        }

        if (removed > 0)
        {
            log?.Invoke($"Removed {removed} root certificate(s) left by CLeARINET.");
        }

        return removed;
    }

    private static List<X509Certificate2> Find(X509Store store) =>
        store.Certificates
            .Find(X509FindType.FindBySubjectName, SubjectPrefix, validOnly: false)
            .Cast<X509Certificate2>()
            .ToList();
}
