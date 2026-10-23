using System;
using System.Diagnostics;

namespace Violens.CompatShim;

/// <summary><c>LogFormat(string, object[])</c>, used by JSFormat through <see cref="FiddlerApplication.Log"/>.</summary>
public sealed class Logger
{
    public void LogFormat(string format, object[] args)
    {
        string message;
        try
        {
            message = string.Format(format, args);
        }
        catch (FormatException)
        {
            // A malformed format string from extension code shouldn't crash
            // the host -- fall back to the raw string.
            message = format;
        }

        Debug.WriteLine("[LegacyExtensionHost] " + message);

        // Also shown on the Log tab. UI may not be set yet early in startup.
        FiddlerApplication.UI?.AppendLog(message);
    }
}
