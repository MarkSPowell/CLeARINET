using Jint;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Loads one <c>CustomRules.js</c>-shaped script and dispatches its
/// <c>Handlers.OnBeforeRequest</c>/<c>OnBeforeResponse</c>/
/// <c>OnPeekAtResponseHeaders</c> against an <see cref="Exchange"/>.
///
/// Runs the script through <b>Jint</b>, not real JScript.NET --
/// <c>Microsoft.JScript</c> (what Fiddler Classic's own script engine
/// compiles <c>CustomRules.js</c> with) was never ported past .NET
/// Framework and isn't available on .NET 10 (open, unresolved:
/// <see href="https://github.com/dotnet/runtime/issues/27155"/>), so there
/// is no way to run an existing script's *actual* compiler here. Jint is a
/// mature, actively maintained, pure-C# ECMAScript interpreter instead --
/// see <see cref="FiddlerScriptPreprocessor"/> for what bridges the syntax
/// gap between JScript.NET's non-standard extensions (typed <c>var</c>
/// declarations, its own class-attribute syntax) and the plain ECMAScript
/// Jint actually parses. This is recorded as a decision in the Project
/// Plan's "Fiddler Classic compatibility review" and the FiddlerScript
/// Compatibility Design doc, not something to second-guess per call site.
///
/// One instance per loaded script (scripts are cheap to reload -- see
/// <c>AppObject.ReloadScript</c> -- so there's no long-lived-engine
/// lifecycle to manage beyond "construct a new one"). Not thread-safe:
/// Jint's <see cref="Engine"/> isn't either, and this codebase's own
/// convention (see <c>BreakpointRules</c>/<c>AutoResponderRules</c>) is
/// plain mutable state with the caller responsible for not calling in from
/// two threads at once -- a future listener integration will need to
/// decide how a single script instance serializes access across
/// concurrently pumped connections, the same way it already has to for
/// <c>SessionStore</c>.
/// </summary>
public sealed class FiddlerScriptHost
{
    // Deliberately unlikely to collide with any real script's own global --
    // real FiddlerScript globals are short, human-typed names
    // (oSession/oEx, Handlers, m_SomeOption), never anything this shape.
    private const string ExchangeGlobalName = "__clearinet_exchange";

    private readonly Engine _engine;

    public FiddlerScriptHost(string scriptSource, AppObject? appObject = null)
    {
        // AllowClr() is what lets a script reference `System.Text.StringBuilder`,
        // `System.Diagnostics.Process`, etc. by dotted name -- several real
        // cookbook samples do exactly this (see the Compatibility Design
        // doc). Unrestricted CLR access from a script is a real trust
        // boundary to be honest about: this is appropriate for a script the
        // person running CLeARINET wrote for themselves (Fiddler Classic's
        // own trust model is identical -- CustomRules.js already runs with
        // the user's own full permissions), not for running an untrusted
        // script from somewhere else.
        _engine = new Engine(options => options.AllowClr());

        var app = appObject ?? new AppObject();
        // Bound under both names since real scripts use either
        // interchangeably depending on which era/fork they were copied
        // from -- see AppObject's own remarks.
        _engine.SetValue("FiddlerObject", app);
        _engine.SetValue("AppObject", app);

        var transpiled = FiddlerScriptPreprocessor.Transpile(scriptSource);
        try
        {
            _engine.Execute(transpiled);
        }
        catch (Exception ex)
        {
            // Deliberately broad, matching RawTextInspector's own
            // "third-party library doesn't document a closed exception set
            // for bad input" reasoning -- Jint can throw its own
            // JavaScriptException for a runtime script error, but a
            // malformed script can also fail during Jint's own parse step,
            // and this project isn't pinning its error handling to Jint's
            // exact internal exception hierarchy for that split. Either
            // way, a script that fails to load should say so clearly, not
            // take the whole host down.
            throw new FiddlerScriptException($"Failed to load FiddlerScript: {ex.Message}", ex);
        }
    }

    /// <summary>Fiddler's <c>OnBeforeRequest</c> -- see the class remarks for what firing this too early/late relative to real Fiddler's own timing would mean; this phase only runs the handler and lets the caller inspect <paramref name="exchange"/> afterward, it doesn't yet feed the result back into a real request/response flow.</summary>
    public void InvokeOnBeforeRequest(Exchange exchange) => Invoke("OnBeforeRequest", exchange);

    /// <summary>Fiddler's <c>OnBeforeResponse</c>.</summary>
    public void InvokeOnBeforeResponse(Exchange exchange) => Invoke("OnBeforeResponse", exchange);

    /// <summary>Fiddler's <c>OnPeekAtResponseHeaders</c>.</summary>
    public void InvokeOnPeekAtResponseHeaders(Exchange exchange) => Invoke("OnPeekAtResponseHeaders", exchange);

    /// <summary>Whether the loaded script defines a given <c>Handlers</c> method -- lets a caller skip building an <see cref="Exchange"/> at all for a handler that was never going to run.</summary>
    public bool HasHandler(string handlerName) =>
        _engine.Evaluate($"typeof Handlers !== 'undefined' && typeof Handlers.{Guard(handlerName)} === 'function'").AsBoolean();

    private void Invoke(string handlerName, Exchange exchange)
    {
        _engine.SetValue(ExchangeGlobalName, exchange);
        try
        {
            _engine.Evaluate(
                $"if (typeof Handlers !== 'undefined' && typeof Handlers.{Guard(handlerName)} === 'function') " +
                $"{{ Handlers.{Guard(handlerName)}({ExchangeGlobalName}); }}");
        }
        catch (Exception ex)
        {
            // Same "broad on purpose" reasoning as the constructor -- a
            // handler throwing partway through (a null-check the script
            // author forgot, a typo'd member name) shouldn't take the
            // caller down with it.
            throw new FiddlerScriptException($"Script error in Handlers.{handlerName}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Every call site here passes one of this class's own three constant
    /// handler names, never anything a script or another caller supplies --
    /// this exists purely as a defense-in-depth assertion against that
    /// invariant quietly breaking later (e.g. a future overload accepting
    /// an arbitrary handler name), not because untrusted input reaches
    /// this today.
    /// </summary>
    private static string Guard(string handlerName)
    {
        if (handlerName is not ("OnBeforeRequest" or "OnBeforeResponse" or "OnPeekAtResponseHeaders"))
        {
            throw new ArgumentException($"Unrecognized FiddlerScript handler name: {handlerName}", nameof(handlerName));
        }

        return handlerName;
    }
}
