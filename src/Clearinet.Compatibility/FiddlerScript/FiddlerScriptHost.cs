using Jint;
using Jint.Native;

namespace Clearinet.Compatibility.FiddlerScript;
// (System.Linq is implicitly available via ImplicitUsings; see the .csproj.)

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
    private const string AssignGlobalName = "__clearinet_assign";
    private const string ContextActionSessionsGlobalName = "__clearinet_context_sessions";
    private const string UiColumnSessionGlobalName = "__clearinet_uicolumn_session";

    private readonly Engine _engine;

    /// <summary>
    /// Every Phase B/C/D menu-binding attribute
    /// <see cref="FiddlerScriptDirectiveScanner"/> found in this script's own
    /// RAW source, scanned once in the constructor -- see that scanner's own
    /// remarks for exactly what it looks for.
    /// <see cref="FiddlerScriptDirectives.Empty"/> for a script that declares
    /// none of these (every script written before Phase B/C/D existed, in
    /// particular).
    /// </summary>
    public FiddlerScriptDirectives Directives { get; }

    public FiddlerScriptHost(string scriptSource, AppObject? appObject = null)
    {
        // Scanned from the RAW source, before FiddlerScriptPreprocessor
        // strips the attribute blocks this reads out of the script below --
        // see FiddlerScriptDirectiveScanner's own remarks on why this has
        // to run first, against text that hasn't been touched yet.
        Directives = FiddlerScriptDirectiveScanner.Scan(scriptSource);

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

    /// <summary>Reads a <see cref="RulesMenuOption"/>'s current value straight off the running script's own <c>Handlers</c> field -- there's no separate cached copy anywhere, so a value a handler itself changed mid-run is always what this returns.</summary>
    public bool GetRulesOptionValue(string fieldName) => EvaluateField(fieldName).AsBoolean();

    /// <summary>Writes a <see cref="RulesMenuOption"/>'s field directly on the running script's <c>Handlers</c> class -- the desktop Rules menu's own "the user just checked/unchecked this" entry point. See <see cref="BindPrefBinding"/>'s own remarks for what happens next if this field is also <c>[BindPref]</c>-bound (nothing here -- persistence is <see cref="FiddlerScriptRunner"/>'s job, one layer up).</summary>
    public void SetRulesOptionValue(string fieldName, bool value) => AssignField(fieldName, value);

    /// <summary>Same as <see cref="GetRulesOptionValue"/>, for a <see cref="RulesMenuStringOption"/>'s <c>String</c> field.</summary>
    public string GetRulesStringValue(string fieldName) => EvaluateField(fieldName).AsString();

    /// <summary>Same as <see cref="SetRulesOptionValue"/>, for a <see cref="RulesMenuStringOption"/>'s <c>String</c> field -- <paramref name="value"/> is expected to be one of that option's own <see cref="RulesStringChoice.Value"/>s, though nothing here enforces that; the desktop UI only ever offers the declared choices.</summary>
    public void SetRulesStringValue(string fieldName, string value) => AssignField(fieldName, value);

    /// <summary>
    /// Calls a <see cref="ContextActionDescriptor"/>'s method with
    /// <paramref name="sessions"/> passed as a single array argument --
    /// real Fiddler's own <c>Session[] oSessions</c> parameter shape,
    /// scoped in this project to whatever the caller hands in (see
    /// <see cref="ContextActionDescriptor"/>'s own remarks on why that's
    /// just the one currently-selected session today). Passed through
    /// Jint's CLR array interop (<c>.length</c>/indexed access), not a
    /// constructed native JS array -- good enough for every real
    /// <c>ContextAction</c> sample fetched while scoping this (a
    /// <c>for</c> loop over <c>oSessions.length</c>), but not a promise
    /// that every <c>Array.prototype</c> method works against it; flagged
    /// here rather than silently assumed.
    /// </summary>
    public void InvokeContextAction(string methodName, IReadOnlyList<Exchange> sessions)
    {
        var guarded = GuardMethodName(methodName, Directives.ContextActions.Select(a => a.MethodName));
        _engine.SetValue(ContextActionSessionsGlobalName, sessions.ToArray());
        try
        {
            _engine.Evaluate($"Handlers.{guarded}({ContextActionSessionsGlobalName});");
        }
        catch (Exception ex)
        {
            throw new FiddlerScriptException($"Script error in ContextAction '{methodName}': {ex.Message}", ex);
        }
    }

    /// <summary>Calls a <see cref="ToolsActionDescriptor"/>'s method with no arguments at all -- real Fiddler's own Tools-menu commands don't operate on a selection.</summary>
    public void InvokeToolsAction(string methodName)
    {
        var guarded = GuardMethodName(methodName, Directives.ToolsActions.Select(a => a.MethodName));
        try
        {
            _engine.Evaluate($"Handlers.{guarded}();");
        }
        catch (Exception ex)
        {
            throw new FiddlerScriptException($"Script error in ToolsAction '{methodName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Calls a <see cref="UIColumnDescriptor"/>'s method with one session
    /// and returns its string result -- real Fiddler's own documentation
    /// warns a well-behaved column method "checks to ensure that objects
    /// exist before use"; this pass can't enforce that either, but a
    /// method that throws anyway is caught here and reported as a
    /// <see cref="FiddlerScriptException"/> rather than taking the whole
    /// grid render down with it (see <see cref="FiddlerScriptRunner.ComputeUIColumnValue"/>
    /// for what the caller shows instead).
    /// </summary>
    public string ComputeUIColumnValue(string methodName, Exchange session)
    {
        var guarded = GuardMethodName(methodName, Directives.UIColumns.Select(c => c.MethodName));
        _engine.SetValue(UiColumnSessionGlobalName, session);
        try
        {
            var result = _engine.Evaluate($"Handlers.{guarded}({UiColumnSessionGlobalName});");
            return result.IsNull() || result.IsUndefined() ? string.Empty : result.ToString();
        }
        catch (Exception ex)
        {
            throw new FiddlerScriptException($"Script error in BindUIColumn '{methodName}': {ex.Message}", ex);
        }
    }

    private JsValue EvaluateField(string fieldName)
    {
        GuardFieldName(fieldName);
        return _engine.Evaluate($"Handlers.{fieldName}");
    }

    private void AssignField(string fieldName, object value)
    {
        GuardFieldName(fieldName);
        try
        {
            _engine.SetValue(AssignGlobalName, value);
            _engine.Execute($"Handlers.{fieldName} = {AssignGlobalName};");
        }
        catch (Exception ex)
        {
            throw new FiddlerScriptException($"Script error setting '{fieldName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Defense in depth, the same role <see cref="Guard"/> plays for handler
    /// names: every real call site passes a field name that came from this
    /// same instance's own <see cref="Directives"/> (what a menu was built
    /// from in the first place), never anything a script or another caller
    /// supplies directly -- this exists to catch that invariant quietly
    /// breaking later, not because untrusted input reaches this today.
    /// </summary>
    private void GuardFieldName(string fieldName)
    {
        var known = Directives.RulesMenuOptions.Select(o => o.FieldName)
            .Concat(Directives.RulesMenuStringOptions.Select(o => o.FieldName))
            .Concat(Directives.BindPrefBindings.Select(b => b.FieldName));

        if (!known.Contains(fieldName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unrecognized FiddlerScript field: {fieldName}", nameof(fieldName));
        }
    }

    /// <summary>Same defense-in-depth reasoning as <see cref="GuardFieldName"/>, for a <see cref="ContextActionDescriptor"/>/<see cref="ToolsActionDescriptor"/>/<see cref="UIColumnDescriptor"/> method name.</summary>
    private static string GuardMethodName(string methodName, IEnumerable<string> knownMethodNames)
    {
        if (!knownMethodNames.Contains(methodName, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unrecognized FiddlerScript method: {methodName}", nameof(methodName));
        }

        return methodName;
    }

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
