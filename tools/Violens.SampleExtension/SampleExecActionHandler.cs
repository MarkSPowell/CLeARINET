using Violens.Compatibility.Extensions;

namespace Violens.SampleExtension;

/// <summary>An original <see cref="IHandleExecAction"/>; type its command into QuickExec.</summary>
public sealed class SampleExecActionHandler : IHandleExecAction
{
    /// <summary>The one command this handler recognizes, case-insensitively.</summary>
    public const string RecognizedCommand = "sample.ping";

    public int RecognizedCallCount { get; private set; }

    /// <summary>Handles <see cref="RecognizedCommand"/> (any case) and returns false for anything else.</summary>
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
