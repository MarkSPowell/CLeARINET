namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Fiddler's <c>FiddlerObject</c> helper, global to every script (not
/// per-exchange, unlike <see cref="Exchange"/>) -- named <c>AppObject</c>
/// here for the same reason <see cref="Exchange"/> isn't called
/// <c>Session</c>: it's what <c>ericlaw1979/Clearinet</c>'s own sample
/// script already calls it (<c>AppObject.ReloadScript()</c>,
/// <c>AppObject.StatusText</c>, <c>AppObject.Log.LogString()</c>), and
/// nothing in script text needs to spell the .NET type name for this to
/// work -- a script just references a global named <c>FiddlerObject</c> or
/// <c>AppObject</c>, whichever <see cref="FiddlerScriptHost"/> binds it as
/// (both, in fact -- see that class's remarks).
///
/// Deliberately thin for this first pass: only what's cheap to implement
/// host-side without any UI or networking dependency. <see cref="prompt"/>
/// and <c>utilIssueRequest</c> (Fiddler's own way to have a script issue a
/// brand-new HTTP request, e.g. for the "Crawl Sequential URLs"/"Find Page
/// Containing Search String" cookbook patterns) need a modal-dialog host
/// and an outbound HTTP client respectively, neither of which this phase
/// wires up -- both throw <see cref="NotSupportedException"/> rather than
/// silently no-op, so a script relying on either fails loudly instead of
/// behaving as if the prompt were always cancelled or the request always
/// failed.
/// </summary>
public sealed class AppObject
{
    private readonly Action<string> _log;

    public AppObject(Action<string>? log = null, Action? reloadScript = null)
    {
        _log = log ?? (_ => { });
        _reloadScript = reloadScript ?? (() => { });
        Log = new AppObjectLog(_log);
    }

    private readonly Action _reloadScript;

    /// <summary>Fiddler's <c>FiddlerObject.StatusText</c> -- the status-bar text a script can set. Plain get/set; wiring this to the desktop app's own <c>StatusText</c> is a UI-layer concern, not this project's.</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>Fiddler's <c>FiddlerObject.Log</c> -- see <see cref="AppObjectLog"/>.</summary>
    public AppObjectLog Log { get; }

    /// <summary>Fiddler's <c>FiddlerObject.alert(sMessage)</c> -- routed through the same logging callback as <see cref="Log"/> rather than a real modal dialog, so a script that calls this during automated/headless use (this project's <see cref="FiddlerScriptHost"/> running outside the desktop app, e.g. under test) doesn't block waiting for a click that will never come.</summary>
    public void alert(string message) => _log($"[FiddlerScript alert] {message}");

    /// <summary>Fiddler's <c>FiddlerObject.playSound(sPath)</c> -- a documented no-op for now; no audio subsystem wired up yet.</summary>
    public void playSound(string path) { }

    /// <summary>Fiddler's <c>FiddlerObject.ReloadScript()</c> -- invokes whatever the host passed as its reload callback; a no-op if none was given.</summary>
    public void ReloadScript() => _reloadScript();

    /// <summary>Fiddler's <c>FiddlerObject.prompt(sMessage, sDefault)</c> -- see this type's own remarks on why this isn't implemented yet.</summary>
    public string prompt(string message, string? defaultValue = null) =>
        throw new NotSupportedException("FiddlerObject.prompt() isn't implemented yet -- see AppObject's own remarks.");
}

/// <summary>Fiddler's <c>FiddlerObject.Log</c> sub-object -- <c>LogString</c>/<c>LogFormat</c> both just format-and-forward to the same callback <see cref="AppObject.alert"/> uses.</summary>
public sealed class AppObjectLog(Action<string> log)
{
    public void LogString(string message) => log(message);

    public void LogFormat(string format, params object?[] args) => log(string.Format(format, args));
}
