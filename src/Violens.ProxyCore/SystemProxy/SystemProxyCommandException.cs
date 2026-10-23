namespace Violens.ProxyCore.SystemProxy;

/// <summary>A <c>networksetup</c> command failed (macOS); carries its exit code and stderr.</summary>
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
