using System;
using System.Diagnostics;

namespace Clearinet.CompatShim;

/// <summary>
/// metadata: <c>Fiddler.Logger.void LogFormat(string, object[])</c> --
/// JSFormat, called as an instance method (obtained via
/// <see cref="FiddlerApplication.Log"/>, matches the confirmed
/// <c>FiddlerApplication.static Fiddler.Logger get_Log()</c>).
/// </summary>
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

        // Also surface on the "Log" tab (see frmViewer.AppendLog) -- this is
        // the same visibility fix Program.cs's own startup logging got, and
        // extensions/this shim's own code (e.g. SazArchive) call LogFormat
        // for exactly the kind of thing that's worth seeing without a
        // debugger attached. FiddlerApplication.UI can be null very early
        // (before Program.cs assigns it) or in a future host that doesn't
        // have a UI at all, so this stays optional rather than required.
        FiddlerApplication.UI?.AppendLog(message);
    }
}
