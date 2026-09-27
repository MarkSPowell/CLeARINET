namespace Clearinet.ProxyCore.Certificates;

/// <summary>
/// Thrown when a shelled-out trust-store command (macOS's <c>security</c>
/// CLI today; nothing on Windows throws this, since
/// <see cref="CertificateAuthority"/>'s own <c>X509Store</c> calls there
/// throw their own native exceptions instead) exits non-zero. Carries the
/// exit code and captured stderr rather than just a generic message, so a
/// caller (or a person reading a log) can actually see what
/// <c>security</c> itself said went wrong -- matches this project's own
/// "surfaced, not silently dropped" posture for failures everywhere else.
/// </summary>
public sealed class CertificateTrustException : Exception
{
    public int ExitCode { get; }

    public string StandardError { get; }

    public CertificateTrustException(string message, int exitCode, string standardError)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}
