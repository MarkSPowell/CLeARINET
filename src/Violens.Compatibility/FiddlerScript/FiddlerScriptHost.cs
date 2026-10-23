using Jint;
using Jint.Native;

namespace Violens.Compatibility.FiddlerScript;
// (System.Linq is implicitly available via ImplicitUsings; see the .csproj.)

/// <summary>
/// Loads one <c>CustomRules.js</c> and runs its <c>Handlers</c> functions
/// against an <see cref="Exchange"/>. Runs on Jint, since JScript.NET isn't
/// available on .NET Core (dotnet/runtime#27155);
/// <see cref="FiddlerScriptPreprocessor"/> handles the syntax differences.
/// One instance per loaded script. Not thread-safe.
/// </summary>
public sealed class FiddlerScriptHost
{
    // A name no real script would use.
    private const string ExchangeGlobalName = "__violens_exchange";
    private const string AssignGlobalName = "__violens_assign";
    private const string ContextActionSessionsGlobalName = "__violens_context_sessions";
    private const string UiColumnSessionGlobalName = "__violens_uicolumn_session";
    private const string ExecActionParamsGlobalName = "__violens_exec_params";

    private readonly Engine _engine;

    /// <summary>The script's menu and column attributes, found when it loaded.</summary>
    public FiddlerScriptDirectives Directives { get; }

    public FiddlerScriptHost(string scriptSource, AppObject? appObject = null)
    {
        // From the raw source, before the preprocessor strips the attributes.
        Directives = FiddlerScriptDirectiveScanner.Scan(scriptSource);

        // AllowClr lets scripts use .NET types by name (System.Text.StringBuilder),
        // as cookbook samples do. It gives the script full access, which is the
        // same trust Fiddler gives CustomRules.js: fine for your own script, not
        // for an untrusted one.
        _engine = new Engine(options => options.AllowClr());

        var app = appObject ?? new AppObject();
        // Scripts use either name.
        _engine.SetValue("FiddlerObject", app);
        _engine.SetValue("AppObject", app);

        var transpiled = FiddlerScriptPreprocessor.Transpile(scriptSource);
        try
        {
            _engine.Execute(transpiled);
        }
        catch (Exception ex)
        {
            // Jint throws different exception types for parse and runtime errors;
            // either way the script didn't load.
            throw new FiddlerScriptException($"Failed to load FiddlerScript: {ex.Message}", ex);
        }
    }

    /// <summary>Fiddler's <c>OnBeforeRequest</c>.</summary>
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

    /// <summary>Sets a <see cref="RulesMenuOption"/>'s field on the running script.</summary>
    public void SetRulesOptionValue(string fieldName, bool value) => AssignField(fieldName, value);

    /// <summary>Same as <see cref="GetRulesOptionValue"/>, for a <see cref="RulesMenuStringOption"/>'s <c>String</c> field.</summary>
    public string GetRulesStringValue(string fieldName) => EvaluateField(fieldName).AsString();

    /// <summary>Same as <see cref="SetRulesOptionValue"/>, for a <see cref="RulesMenuStringOption"/>'s <c>String</c> field -- <paramref name="value"/> is expected to be one of that option's own <see cref="RulesStringChoice.Value"/>s, though nothing here enforces that; the desktop UI only ever offers the declared choices.</summary>
    public void SetRulesStringValue(string fieldName, string value) => AssignField(fieldName, value);

    /// <summary>
    /// Calls a <c>ContextAction</c> method with the sessions as one array, as
    /// Fiddler's <c>Session[]</c> parameter. It's a CLR array, so
    /// <c>.length</c> and indexing work but not every Array method.
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

    /// <summary>
    /// Fiddler's <c>OnExecAction(sParams: String[]): Boolean</c>: a QuickExec
    /// command, split into words, offered to the script. True when the script
    /// handled it (returned true); false when it didn't, or has no
    /// <c>OnExecAction</c>.
    /// </summary>
    public bool InvokeOnExecAction(IReadOnlyList<string> words)
    {
        if (!HasHandler("OnExecAction"))
        {
            return false;
        }

        _engine.SetValue(ExecActionParamsGlobalName, words.ToArray());
        try
        {
            var result = _engine.Evaluate($"Handlers.OnExecAction({ExecActionParamsGlobalName});");
            return result.IsBoolean() && result.AsBoolean();
        }
        catch (Exception ex)
        {
            throw new FiddlerScriptException($"Script error in OnExecAction: {ex.Message}", ex);
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
    /// Calls a <c>BindUIColumn</c> method for one session. A method that throws
    /// is reported as a <see cref="FiddlerScriptException"/>.
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

    /// <summary>A safety check: field names only ever come from this script's own <see cref="Directives"/>.</summary>
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
            // A handler error mustn't take the caller down.
            throw new FiddlerScriptException($"Script error in Handlers.{handlerName}: {ex.Message}", ex);
        }
    }

    /// <summary>A safety check: handler names are only ever this class's own constants.</summary>
    private static string Guard(string handlerName)
    {
        if (handlerName is not ("OnBeforeRequest" or "OnBeforeResponse" or "OnPeekAtResponseHeaders" or "OnExecAction"))
        {
            throw new ArgumentException($"Unrecognized FiddlerScript handler name: {handlerName}", nameof(handlerName));
        }

        return handlerName;
    }
}
