namespace Violens.ProxyCore.Breakpoints;

/// <summary>
/// Thrown when a paused session is aborted rather than resumed. Fiddler
/// Classic's own docs don't describe a distinct "abort" action separate
/// from just not resuming (see the breakpoints research notes); VioLens
/// makes it explicit instead -- <c>Violens.ProxyCore.Proxy.InterceptingProxyListener</c>
/// catches this and tears the connection down, the same end state a killed
/// session reaches either way.
/// </summary>
public sealed class BreakpointAbortedException(string host, BreakpointStage stage, string method, string target)
    : Exception($"Session aborted at a breakpoint ({stage}): {method} https://{host}{target}")
{
    public string Host { get; } = host;

    public BreakpointStage Stage { get; } = stage;

    public string Method { get; } = method;

    public string Target { get; } = target;
}
