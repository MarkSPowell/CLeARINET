using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Scripting;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Implements <see cref="IFiddlerScriptRunner"/> over a loaded
/// <see cref="FiddlerScriptHost"/> -- the concrete half of the
/// interface-inversion described in that interface's own remarks. Owns the
/// whole load/reload lifecycle (a file path plus the current
/// <see cref="FiddlerScriptHost"/> instance), so a composition root only
/// has to construct one of these once and keep calling
/// <see cref="LoadFromFile"/>/<see cref="Reload"/> on it across a run --
/// the same way it already owns one long-lived <c>BreakpointManager</c>/
/// <c>AutoResponderRules</c> instance across Start/Stop cycles (see
/// <c>MainWindowViewModel</c>).
///
/// <b>Not thread-safe</b>, for the same reason <see cref="FiddlerScriptHost"/>
/// itself isn't -- see that class's own remarks. <c>InterceptingProxyListener</c>
/// pumps one connection per <c>Task</c> with no serialization of its own
/// around a shared <see cref="IFiddlerScriptRunner"/>, so a script reload
/// that races a concurrent <see cref="RunOnBeforeRequest"/>/
/// <see cref="RunOnBeforeResponse"/> call on another connection is a real,
/// accepted gap in this pass -- flagged in the design doc, not silently
/// ignored. In practice this mirrors how a live AutoResponder rule edit
/// already races the very next request under this codebase's existing
/// "no formal transaction" concurrency posture.
/// </summary>
public sealed class FiddlerScriptRunner : IFiddlerScriptRunner
{
    private readonly Action<string> _log;
    private readonly FiddlerScriptPreferenceStore _prefStore;
    private FiddlerScriptHost? _host;
    private string? _loadedPath;

    /// <param name="log">
    /// Where <c>FiddlerObject.alert()</c>/<c>FiddlerObject.Log.LogString()</c>
    /// calls from inside a running script go, plus this runner's own
    /// handler-error notices -- see <see cref="RunOnBeforeRequest"/>'s
    /// remarks. Defaults to a no-op; a desktop host passes something that
    /// reaches its own status/log surface.
    /// </param>
    /// <param name="preferenceStore">
    /// Backs every <see cref="BindPrefBinding"/> this runner ever loads --
    /// defaults to a real, on-disk <see cref="FiddlerScriptPreferenceStore"/>;
    /// overridable purely for tests. One instance is kept for this runner's
    /// whole lifetime (constructed here, never replaced by
    /// <see cref="LoadFromSource"/>), which is what gives an
    /// <c>Ephemeral</c>-named preference its own documented "survives a
    /// reload, not a restart" lifetime -- see that class's own remarks.
    /// </param>
    public FiddlerScriptRunner(Action<string>? log = null, FiddlerScriptPreferenceStore? preferenceStore = null)
    {
        _log = log ?? (_ => { });
        _prefStore = preferenceStore ?? new FiddlerScriptPreferenceStore();
    }

    /// <summary>True once a script has loaded successfully. Stays true across a *failed* reload -- see <see cref="LoadFromSource"/>'s own remarks on why a bad edit doesn't drop the previously-working script.</summary>
    public bool IsLoaded => _host is not null;

    /// <summary>The path last given to <see cref="LoadFromFile"/>, if any -- lets a caller re-display "loaded: C:\...\CustomRules.js" without keeping its own copy.</summary>
    public string? LoadedPath => _loadedPath;

    /// <summary>
    /// Set by <see cref="LoadFromFile"/>/<see cref="LoadFromSource"/> when
    /// the most recent load attempt failed a parse or a script-level error
    /// during <c>FiddlerScriptHost</c>'s own load step; <see langword="null"/>
    /// after a successful load. Doesn't distinguish "never loaded anything"
    /// from "loaded fine" -- check <see cref="IsLoaded"/> for that.
    /// </summary>
    public string? LoadError { get; private set; }

    /// <inheritdoc/>
    public bool HasOnBeforeRequest { get; private set; }

    /// <inheritdoc/>
    public bool HasOnBeforeResponse { get; private set; }

    /// <summary>
    /// The currently-loaded script's own Phase B/C/D declarations --
    /// <see cref="FiddlerScriptDirectives.Empty"/> whenever
    /// <see cref="IsLoaded"/> is <see langword="false"/>, so a caller never
    /// has to null-check this separately from checking that.
    /// </summary>
    public FiddlerScriptDirectives Directives => _host?.Directives ?? FiddlerScriptDirectives.Empty;

    /// <summary>
    /// Reads and loads the script at <paramref name="path"/>, remembering
    /// the path so <see cref="Reload"/> can re-read it later -- Fiddler's
    /// own <c>FiddlerObject.ReloadScript()</c> (see <see cref="AppObject"/>'s
    /// remarks) needs exactly this, and this runner wires its own
    /// <see cref="Reload"/> as that callback (see <see cref="LoadFromSource"/>).
    /// Propagates <see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>
    /// straight out (the path itself being unreadable is a different failure
    /// than the script inside it being broken -- see <see cref="LoadError"/>,
    /// which is reserved for the latter) so a caller can tell the two apart.
    /// </summary>
    public void LoadFromFile(string path)
    {
        var source = File.ReadAllText(path);
        _loadedPath = path;
        LoadFromSource(source);
    }

    /// <summary>
    /// Re-reads and reloads the script at whatever path
    /// <see cref="LoadFromFile"/> was last given. A no-op if nothing has
    /// been loaded from a file yet (never throws in that case -- there's
    /// nothing to reload, which isn't itself an error).
    /// </summary>
    public void Reload()
    {
        if (_loadedPath is not null)
        {
            LoadFromFile(_loadedPath);
        }
    }

    /// <summary>
    /// Loads directly from source text, without a backing file -- what
    /// <see cref="LoadFromFile"/> itself calls, and available on its own for
    /// tests or a future "paste a script" UI that has no file at all. A
    /// failed load (bad syntax, a runtime error thrown while the script's
    /// own top-level code executes) leaves whatever script was already
    /// running, if any, in place rather than dropping this runner to "no
    /// script loaded" -- matching real Fiddler's own behavior, where a
    /// broken <c>CustomRules.js</c> edit surfaces an error dialog without
    /// disabling the proxy or discarding the last-good script.
    /// <see cref="LoadError"/> carries the failure for a caller to surface;
    /// <see cref="HasOnBeforeRequest"/>/<see cref="HasOnBeforeResponse"/>
    /// are left exactly as they were before this call on failure, for the
    /// same "keep running the last-good script" reason.
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
    public FiddlerScriptRequestResult RunOnBeforeRequest(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (_host is null || !HasOnBeforeRequest)
        {
            return new FiddlerScriptRequestResult(request);
        }

        var exchange = Exchange.ForRequest(sessionOrdinal, hostname, request);
        try
        {
            _host.InvokeOnBeforeRequest(exchange);
        }
        catch (FiddlerScriptException ex)
        {
            // A handler that throws partway through shouldn't take the
            // connection down with it -- log and fall back to the request
            // exactly as it was handed in, the same "a script error isn't a
            // proxy error" posture FiddlerScriptHost's own remarks describe
            // for the load step. Whatever the script already mutated on
            // `exchange` before throwing is deliberately discarded here
            // rather than partially applied -- ToRequest() is never called
            // on this path.
            _log($"[FiddlerScript] OnBeforeRequest error: {ex.Message}");
            return new FiddlerScriptRequestResult(request);
        }

        return new FiddlerScriptRequestResult(exchange.ToRequest());
    }

    /// <inheritdoc/>
    public FiddlerScriptResponseResult RunOnBeforeResponse(
        int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (_host is null || !HasOnBeforeResponse)
        {
            return new FiddlerScriptResponseResult(response);
        }

        var exchange = Exchange.ForResponse(sessionOrdinal, hostname, request, response);
        try
        {
            _host.InvokeOnBeforeResponse(exchange);
        }
        catch (FiddlerScriptException ex)
        {
            _log($"[FiddlerScript] OnBeforeResponse error: {ex.Message}");
            return new FiddlerScriptResponseResult(response);
        }

        return new FiddlerScriptResponseResult(exchange.ToResponse());
    }

    /// <summary>See <see cref="FiddlerScriptHost.GetRulesOptionValue"/>. Returns <see langword="false"/> when nothing is loaded, rather than throwing -- a menu built from <see cref="Directives"/> (empty when unloaded) would never call this in that state anyway, but a caller shouldn't have to know that to stay safe.</summary>
    public bool GetRulesOptionValue(string fieldName) => _host?.GetRulesOptionValue(fieldName) ?? false;

    /// <summary>See <see cref="FiddlerScriptHost.SetRulesOptionValue"/>. Also persists through <see cref="FiddlerScriptPreferenceStore"/> if this field carries a <see cref="BindPrefBinding"/> -- see that record's own remarks on exactly when this round-trips and when it doesn't.</summary>
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

    /// <summary>See <see cref="FiddlerScriptHost.SetRulesStringValue"/> and <see cref="SetRulesOptionValue"/>'s own remarks on persistence.</summary>
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
    /// See <see cref="FiddlerScriptHost.InvokeContextAction"/>. Builds one
    /// <see cref="Exchange"/> per session (via <see cref="Exchange.ForResponse"/>,
    /// the same factory <see cref="RunOnBeforeResponse"/> uses for an
    /// in-flight response) -- unlike that method, nothing here ever calls
    /// <c>ToResponse()</c> back on the result, so any edit a
    /// <c>ContextAction</c> makes to a session's headers/body is visible only
    /// to the action's own run, never written back to
    /// <c>SessionStore</c>. A real, flagged limitation -- see
    /// <see cref="ContextActionDescriptor"/>'s own remarks on the
    /// single-session scope cut this shares.
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

    /// <summary>
    /// See <see cref="FiddlerScriptHost.ComputeUIColumnValue"/>. Returns
    /// <c>"(error)"</c> rather than throwing when the script's own column
    /// method fails -- a broken column shouldn't stop the rest of the grid
    /// from rendering, and the error itself still reaches
    /// <see cref="_log"/> for whoever's watching the console/status panel.
    /// </summary>
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

    /// <summary>
    /// Loads every <see cref="BindPrefBinding"/> the just-loaded script
    /// declares out of <see cref="_prefStore"/> and applies whichever ones
    /// already have a persisted value onto the script's own field -- run
    /// once, right after a successful <see cref="LoadFromSource"/>. A
    /// binding with no persisted value yet (first time this preference has
    /// ever been set) is left exactly as the script's own field initializer
    /// set it; there's nothing to apply.
    /// </summary>
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
