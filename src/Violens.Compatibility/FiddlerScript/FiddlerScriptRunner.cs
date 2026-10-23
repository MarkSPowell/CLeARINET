using Violens.ProxyCore.Http;
using Violens.ProxyCore.Scripting;
using Violens.ProxyCore.Sessions;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// The <see cref="IScriptRunner"/> for FiddlerScript: owns the loaded
/// script and its reloads, so the app creates one and keeps it. Not
/// thread-safe: a reload can race a request on another connection, as
/// editing an AutoResponder rule can.
/// </summary>
public sealed class FiddlerScriptRunner : IScriptRunner
{
    private readonly Action<string> _log;
    private readonly FiddlerScriptPreferenceStore _prefStore;
    private FiddlerScriptHost? _host;
    private string? _loadedPath;

    /// <param name="log">Where script alerts, log calls and handler errors go.</param>
    /// <param name="preferenceStore">
    /// Saved <c>[BindPref]</c> values; replaceable for tests. Kept for the
    /// runner's lifetime, which is what lets "Ephemeral" values survive a reload.
    /// </param>
    public FiddlerScriptRunner(Action<string>? log = null, FiddlerScriptPreferenceStore? preferenceStore = null)
    {
        _log = log ?? (_ => { });
        _prefStore = preferenceStore ?? new FiddlerScriptPreferenceStore();
    }

    /// <summary>True once a script has loaded. A failed reload keeps the previous script.</summary>
    public bool IsLoaded => _host is not null;

    /// <summary>The path last given to <see cref="LoadFromFile"/>, if any -- lets a caller re-display "loaded: C:\...\CustomRules.js" without keeping its own copy.</summary>
    public string? LoadedPath => _loadedPath;

    /// <summary>Why the last load failed, or null if it succeeded.</summary>
    public string? LoadError { get; private set; }

    /// <inheritdoc/>
    public bool HasOnBeforeRequest { get; private set; }

    /// <inheritdoc/>
    public bool HasOnBeforeResponse { get; private set; }

    /// <summary>The loaded script's menu and column attributes; empty if none is loaded.</summary>
    public FiddlerScriptDirectives Directives => _host?.Directives ?? FiddlerScriptDirectives.Empty;

    /// <summary>
    /// Loads the script at <paramref name="path"/> and remembers it for
    /// <see cref="Reload"/>. A file that can't be read throws; a broken script
    /// sets <see cref="LoadError"/> instead.
    /// </summary>
    public void LoadFromFile(string path)
    {
        var source = File.ReadAllText(path);
        _loadedPath = path;
        LoadFromSource(source);
    }

    /// <summary>Reloads from the last file loaded; does nothing if there isn't one.</summary>
    public void Reload()
    {
        if (_loadedPath is not null)
        {
            LoadFromFile(_loadedPath);
        }
    }

    /// <summary>
    /// Loads from source text. If it fails, the previous script keeps running
    /// and <see cref="LoadError"/> says why, as Fiddler keeps the last good
    /// script.
    /// </summary>
    public void LoadFromSource(string source)
    {
        try
        {
            var app = new AppObject(log: _log, reloadScript: Reload);
            var host = new FiddlerScriptHost(source, app);
            _host = host;
            HasOnBeforeRequest = host.HasHandler("OnBeforeRequest");
            HasOnBeforeResponse = host.HasHandler("OnBeforeResponse");
            ApplyPersistedPreferences(host);
            LoadError = null;
        }
        catch (FiddlerScriptException ex)
        {
            LoadError = ex.Message;
        }
    }

    /// <inheritdoc/>
    public ScriptRequestResult RunOnBeforeRequest(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (_host is null || !HasOnBeforeRequest)
        {
            return new ScriptRequestResult(request);
        }

        var exchange = Exchange.ForRequest(sessionOrdinal, hostname, request);
        try
        {
            _host.InvokeOnBeforeRequest(exchange);
        }
        catch (FiddlerScriptException ex)
        {
            // A handler error is logged and the request goes on unchanged; partial
            // edits are discarded.
            _log($"[FiddlerScript] OnBeforeRequest error: {ex.Message}");
            return new ScriptRequestResult(request);
        }

        return new ScriptRequestResult(exchange.ToRequest());
    }

    /// <inheritdoc/>
    public ScriptResponseResult RunOnBeforeResponse(
        int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (_host is null || !HasOnBeforeResponse)
        {
            return new ScriptResponseResult(response);
        }

        var exchange = Exchange.ForResponse(sessionOrdinal, hostname, request, response);
        try
        {
            _host.InvokeOnBeforeResponse(exchange);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] OnBeforeResponse error: {ex.Message}");
            return new ScriptResponseResult(response);
        }

        return new ScriptResponseResult(exchange.ToResponse());
    }

    /// <summary>See <see cref="FiddlerScriptHost.GetRulesOptionValue"/>. Returns <see langword="false"/> when nothing is loaded, rather than throwing -- a menu built from <see cref="Directives"/> (empty when unloaded) would never call this in that state anyway, but a caller shouldn't have to know that to stay safe.</summary>
    public bool GetRulesOptionValue(string fieldName) => _host?.GetRulesOptionValue(fieldName) ?? false;

    /// <summary>Sets a Rules option, and saves it if the field has <c>[BindPref]</c>.</summary>
    public void SetRulesOptionValue(string fieldName, bool value)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            _host.SetRulesOptionValue(fieldName, value);
            PersistIfBound(fieldName, value ? "true" : "false");
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] Setting '{fieldName}' failed: {ex.Message}");
        }
    }

    /// <summary>See <see cref="FiddlerScriptHost.GetRulesStringValue"/>. Returns <see cref="string.Empty"/> when nothing is loaded -- same reasoning as <see cref="GetRulesOptionValue"/>.</summary>
    public string GetRulesStringValue(string fieldName) => _host?.GetRulesStringValue(fieldName) ?? string.Empty;

    /// <summary>Sets a Rules string choice, and saves it if the field has <c>[BindPref]</c>.</summary>
    public void SetRulesStringValue(string fieldName, string value)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            _host.SetRulesStringValue(fieldName, value);
            PersistIfBound(fieldName, value);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] Setting '{fieldName}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a <c>ContextAction</c> on the sessions. Edits the action makes
    /// aren't saved back to the session list.
    /// </summary>
    public void InvokeContextAction(string methodName, IReadOnlyList<Session> sessions)
    {
        if (_host is null)
        {
            return;
        }

        var exchanges = sessions
            .Select(s => Exchange.ForResponse(s.Id, s.Host, s.Request, s.Response))
            .ToArray();
        try
        {
            _host.InvokeContextAction(methodName, exchanges);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] ContextAction '{methodName}' error: {ex.Message}");
        }
    }

    /// <summary>
    /// See <see cref="FiddlerScriptHost.InvokeOnExecAction"/>. A script error
    /// is logged and counts as not handled.
    /// </summary>
    public bool InvokeOnExecAction(IReadOnlyList<string> words)
    {
        if (_host is null)
        {
            return false;
        }

        try
        {
            return _host.InvokeOnExecAction(words);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] OnExecAction error: {ex.Message}");
            return false;
        }
    }

    /// <summary>See <see cref="FiddlerScriptHost.InvokeToolsAction"/>.</summary>
    public void InvokeToolsAction(string methodName)
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            _host.InvokeToolsAction(methodName);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] ToolsAction '{methodName}' error: {ex.Message}");
        }
    }

    /// <summary>A script-defined column's value, or "(error)" if the method throws (the error is logged).</summary>
    public string ComputeUIColumnValue(string methodName, Session session)
    {
        if (_host is null)
        {
            return string.Empty;
        }

        var exchange = Exchange.ForResponse(session.Id, session.Host, session.Request, session.Response);
        try
        {
            return _host.ComputeUIColumnValue(methodName, exchange);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] BindUIColumn '{methodName}' error: {ex.Message}");
            return "(error)";
        }
    }

    /// <summary>Applies saved <c>[BindPref]</c> values to the newly loaded script's fields.</summary>
    private void ApplyPersistedPreferences(FiddlerScriptHost host)
    {
        foreach (var binding in host.Directives.BindPrefBindings)
        {
            var persisted = _prefStore.Get(binding.PrefName);
            if (persisted is null)
            {
                continue;
            }

            try
            {
                if (binding.Kind == FieldValueKind.Boolean && bool.TryParse(persisted, out var boolValue))
                {
                    host.SetRulesOptionValue(binding.FieldName, boolValue);
                }
                else if (binding.Kind == FieldValueKind.String)
                {
                    host.SetRulesStringValue(binding.FieldName, persisted);
                }
            }
            catch (FiddlerScriptException ex)
            {
                _log($"[FiddlerScript] Restoring preference '{binding.PrefName}' onto '{binding.FieldName}' failed: {ex.Message}");
            }
        }
    }

    private void PersistIfBound(string fieldName, string value)
    {
        var binding = Directives.BindPrefBindings.FirstOrDefault(b => b.FieldName == fieldName);
        if (binding is not null)
        {
            _prefStore.Set(binding.PrefName, value);
        }
    }
}
