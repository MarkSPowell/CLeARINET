namespace Violens.ProxyCore.Breakpoints;

/// <summary>Thrown when a paused session is aborted; the listener closes the connection.</summary>
public sealed class BreakpointAbortedException(string host, BreakpointStage stage, string method, string target)
    : Exception($"Session aborted at a breakpoint ({stage}): {method} https://{host}{target}")
{
    public string Host { get; } = host;

    public BreakpointStage Stage { get; } = stage;

    public string Method { get; } = method;

    public string Target { get; } = target;
}
