using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace Clearinet.ProxyCore.Certificates;

/// <summary>
/// The macOS half of "Installing trust" in the Interception Certificate
/// Design doc's own "Platform status" section -- see that section for the
/// full reasoning behind every choice here (why shelling out to the
/// <c>security</c> CLI rather than P/Invoke-ing <c>Security.framework</c>
/// directly, why the user's default keychain rather than a hardcoded path,
/// why <c>-Z</c>-hash matching for removal) and for the honest list of
/// what's still unconfirmed without a real Mac to run this against.
/// Called from <see cref="CertificateAuthority"/>'s own
/// <c>EnsureTrusted</c>/<c>Uninstall</c>, exactly where the Windows
/// <c>X509Store(StoreName.Root, ...)</c> calls already were -- see that
/// class's own remarks for the per-platform dispatch.
/// </summary>
public static class MacOSCertificateTrust
{
    /// <summary>
    /// Exports <paramref name="rootCertificate"/> to a temporary
    /// DER-encoded file and runs <c>security add-trusted-cert</c> against
    /// it, then deletes the temp file in a <c>finally</c> whether or not
    /// the command succeeded. Throws <see cref="CertificateTrustException"/>
    /// on a non-zero exit -- never silently swallowed.
    ///
    /// <b>Never silent by CLeARINET's own doing, even though this command
    /// itself has no OS-level confirmation prompt the way Windows'
    /// <c>X509Store.Add</c> does:</b> the design doc's "silent-install
    /// tension" section is explicit that the caller (<c>MainWindow</c>) is
    /// responsible for its own confirmation dialog before this method is
    /// ever reached on macOS -- this method itself has no opinion on
    /// consent, it just performs the install once asked to.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void Install(X509Certificate2 rootCertificate)
    {
        var tempCertPath = Path.Combine(Path.GetTempPath(), $"clearinet-root-{Guid.NewGuid():N}.cer");

        try
        {
            File.WriteAllBytes(tempCertPath, rootCertificate.Export(X509ContentType.Cert));
            RunSecurityCommand(BuildAddTrustedCertArguments(tempCertPath));
        }
        finally
        {
            try
            {
                File.Delete(tempCertPath);
            }
            catch
            {
                // Best-effort cleanup of a temp file in the OS temp
                // directory -- not worth failing an otherwise-successful
                // (or already-failed) install over. Matches
                // WinInetSystemProxy.DeleteBackupFile's own posture on
                // this exact kind of non-load-bearing cleanup.
            }
        }
    }

    /// <summary>
    /// Runs <c>security delete-certificate -Z &lt;thumbprint&gt;</c> --
    /// matched by SHA-1 hash, not common name, so there's no ambiguity if
    /// more than one CLeARINET root ever ends up in the same keychain (an
    /// old one from a previous install that was never cleanly uninstalled,
    /// say). Throws <see cref="CertificateTrustException"/> on a non-zero
    /// exit; per <c>security</c>'s own documented behavior a "no such
    /// certificate" exit is expected and harmless if this is called for a
    /// root that was never actually installed (or already removed) -- that
    /// still surfaces as an exception here rather than being swallowed,
    /// matching <see cref="CertificateAuthority.Uninstall"/>'s own
    /// not-yet-forgiving-of-a-double-call shape on the Windows side.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void Uninstall(X509Certificate2 rootCertificate) =>
        RunSecurityCommand(BuildDeleteCertificateArguments(rootCertificate.Thumbprint));

    /// <summary>
    /// No <c>-k</c> (keychain path): defaults to the user's own default
    /// keychain, normally <c>login.keychain-db</c>, rather than hardcoding
    /// that path, which can legitimately differ. No <c>-d</c>: that flag
    /// specifically adds the *admin* cert store (system-wide trust, needs
    /// elevation) alongside the default keychain -- omitting it is what
    /// keeps this per-user and unelevated, matching the Windows
    /// <c>CurrentUser</c> design. <c>-r trustRoot</c>: trust this as a
    /// root CA for every policy, the same blanket trust
    /// <c>X509Store(StoreName.Root, ...).Add</c> grants on Windows.
    /// Internal (not private) purely so
    /// <c>MacOSCertificateTrustTests</c> can assert on the exact argument
    /// list without actually invoking <c>security</c> -- this project has
    /// no macOS machine to run that against from this session; see the
    /// design doc's own "Testing scope, deliberately narrow" note.
    /// </summary>
    internal static string[] BuildAddTrustedCertArguments(string certificateFilePath) =>
        ["add-trusted-cert", "-r", "trustRoot", certificateFilePath];

    /// <summary>
    /// <c>-Z</c> matches by SHA-1 hash -- <see cref="X509Certificate2.Thumbprint"/>
    /// is already the uppercase hex string <c>security</c> expects for it,
    /// no reformatting needed. See <see cref="BuildAddTrustedCertArguments"/>'s
    /// own remarks on why this is <c>internal</c>.
    /// </summary>
    internal static string[] BuildDeleteCertificateArguments(string sha1Thumbprint) =>
        ["delete-certificate", "-Z", sha1Thumbprint];

    [SupportedOSPlatform("macos")]
    private static void RunSecurityCommand(string[] arguments)
    {
        var startInfo = new ProcessStartInfo("security")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new CertificateTrustException("Failed to start the 'security' process.", exitCode: -1, standardError: string.Empty);

        // Both streams read to completion before WaitForExit, not after:
        // security's own stdout/stderr buffers are small enough that a
        // sequential "wait then read" risks a classic deadlock if it ever
        // writes enough to either stream to fill the OS pipe buffer before
        // exiting. Reading fully first, then waiting, avoids that -- the
        // process has already produced all its output by the time
        // ReadToEnd returns for a well-behaved, short-lived CLI tool like
        // this one.
        var standardError = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new CertificateTrustException(
                $"'security {string.Join(' ', arguments)}' exited with code {process.ExitCode}.",
                process.ExitCode,
                standardError);
        }
    }
}
