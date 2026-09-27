namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Wraps a script-side failure (a parse error in the user's own
/// <c>CustomRules.js</c>, or an exception thrown while a handler runs)
/// behind one exception type callers can catch without depending on
/// Jint's own exception hierarchy directly. See <see cref="FiddlerScriptHost"/>'s
/// remarks for why the catch around Jint calls is broad.
/// </summary>
public sealed class FiddlerScriptException(string message, Exception innerException)
    : Exception(message, innerException)
{
}
