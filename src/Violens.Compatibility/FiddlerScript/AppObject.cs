namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// Fiddler's <c>FiddlerObject</c>, global to every script; scripts can use
/// either name. <see cref="prompt"/> and <c>utilIssueRequest</c> aren't
/// implemented and throw <see cref="NotSupportedException"/>, so scripts
/// that need them fail visibly.
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

    /// <summary>Fiddler's <c>FiddlerObject.prompt</c>. Not implemented.</summary>
    public string prompt(string message, string? defaultValue = null) =>
        throw new NotSupportedException("FiddlerObject.prompt() isn't implemented yet -- see AppObject's own remarks.");
}

/// <summary>Fiddler's <c>FiddlerObject.Log</c> sub-object -- <c>LogString</c>/<c>LogFormat</c> both just format-and-forward to the same callback <see cref="AppObject.alert"/> uses.</summary>
public sealed class AppObjectLog(Action<string> log)
{
    public void LogString(string message) => log(message);

    public void LogFormat(string format, params object?[] args) => log(string.Format(format, args));
}
