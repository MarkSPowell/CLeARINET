using Clearinet.Compatibility.Extensions;

namespace Clearinet.SampleExtension;

/// <summary>
/// An original <see cref="IHandleExecAction"/> implementation. CLeARINET
/// has no QuickExec-equivalent UI to actually type a command into yet (see
/// that interface's own remarks), so nothing in the desktop app calls this
/// today -- it exists purely so the contract itself is exercised, by
/// <c>Clearinet.SampleExtension.Tests.SampleExecActionHandlerTests</c>,
/// calling <see cref="OnExecAction"/> directly.
/// </summary>
public sealed class SampleExecActionHandler : IHandleExecAction
{
    /// <summary>The one command this handler recognizes, case-insensitively.</summary>
    public const string RecognizedCommand = "sample.ping";

    public int RecognizedCallCount { get; private set; }

    /// <summary>
    /// Recognizes exactly <see cref="RecognizedCommand"/>, case-insensitively,
    /// and returns <see langword="false"/> for anything else -- matching
    /// this project's own documented true/false convention for
    /// <c>IHandleExecAction.OnExecAction</c> (see that interface's own
    /// remarks: <see langword="true"/> means "I handled it," letting a
    /// future multi-extension QuickExec dispatch move on to the next
    /// handler on <see langword="false"/>).
    /// </summary>
    public bool OnExecAction(string sCommand)
    {
        if (!string.Equals(sCommand, RecognizedCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RecognizedCallCount++;
        Console.WriteLine("[SampleExtension] OnExecAction: sample.ping recognized.");
        return true;
    }
}
