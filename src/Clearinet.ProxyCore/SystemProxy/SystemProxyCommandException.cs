namespace Clearinet.ProxyCore.SystemProxy;

/// <summary>
/// Thrown when a shelled-out <c>networksetup</c> invocation (the macOS half
/// of system proxy registration; nothing on Windows throws this, since
/// <see cref="WinInetSystemProxy"/> talks to the registry directly with no
/// subprocess involved) exits non-zero. Carries the exit code and captured
/// stderr rather than just a generic message, matching
/// <c>Certificates.CertificateTrustException</c>'s own shape for the
/// equivalent situation on the Keychain side -- see that class's remarks for
/// why this project surfaces shelled-out command failures rather than
/// swallowing them.
/// </summary>
public sealed class SystemProxyCommandException : Exception
{
    public int ExitCode { get; }

    public string StandardError { get; }

    public SystemProxyCommandException(string message, int exitCode, string standardError)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}
