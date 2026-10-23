namespace Violens.Compatibility.FiddlerScript;

/// <summary>A script failed to load or a handler threw. Wraps Jint's exceptions.</summary>
public sealed class FiddlerScriptException(string message, Exception innerException)
    : Exception(message, innerException)
{
}
