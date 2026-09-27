using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Implements <see cref="IExtensionAutoTamperHost"/> over a fixed list of
/// already-constructed <see cref="IAutoTamper"/> instances -- the pure,
/// testable half of the extension-host split (mirroring
/// <see cref="Clearinet.Compatibility.FiddlerScript.FiddlerScriptRunner"/>'s
/// own split from <c>FiddlerScriptHost</c>: that class owns Jint/script
/// parsing, this one owns none of the disk/reflection/<c>AssemblyLoadContext</c>
/// scanning <see cref="ExtensionHost"/> does -- it just runs hooks against
/// whatever instances it's handed). Tests construct this directly with
/// hand-written fake <see cref="IAutoTamper"/> implementations, with no need
/// for a real compiled extension .dll anywhere in the test suite.
///
/// Builds one <see cref="Exchange"/> per call and runs every loaded
/// extension's hook against that SAME instance in sequence -- so, exactly
/// like real Fiddler running several loaded extensions' <c>AutoTamperRequestBefore</c>
/// against one shared <c>oSession</c>, each extension sees the previous
/// one's edits already applied. The final, possibly multiply-edited state is
/// projected back to an immutable record only once, after every extension
/// has run.
/// </summary>
public sealed class LoadedExtensionSet : IExtensionAutoTamperHost
{
    private readonly IReadOnlyList<IAutoTamper> _tampers;
    private readonly Action<string> _log;

    /// <param name="tampers">Every loaded extension that implements <see cref="IAutoTamper"/>, in discovery order -- see <see cref="ExtensionHost"/> for how that list gets built from disk.</param>
    /// <param name="log">Where a hook that throws gets logged -- see <see cref="RunRequestBefore"/>'s own remarks. Defaults to a no-op.</param>
    public LoadedExtensionSet(IReadOnlyList<IAutoTamper> tampers, Action<string>? log = null)
    {
        _tampers = tampers;
        _log = log ?? (_ => { });
    }

    /// <inheritdoc/>
    public bool HasAnyRequestBeforeHandlers => _tampers.Count > 0;

    /// <inheritdoc/>
    public bool HasAnyResponseBeforeHandlers => _tampers.Count > 0;

    /// <inheritdoc/>
    public CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (_tampers.Count == 0)
        {
            return request;
        }

        var exchange = Exchange.ForRequest(sessionOrdinal, hostname, request);
        foreach (var tamper in _tampers)
        {
            RunSafely(tamper, "AutoTamperRequestBefore", () => tamper.AutoTamperRequestBefore(exchange));
        }

        return exchange.ToRequest();
    }

    /// <inheritdoc/>
    public void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (_tampers.Count == 0)
        {
            return;
        }

        // Fire-and-observe -- see this interface's own remarks on why the
        // result is never projected back. Still built from the real, final
        // request (not the pre-edit one) so an extension's *After hook sees
        // what actually went out, matching real Fiddler's own timing.
        var exchange = Exchange.ForRequest(sessionOrdinal, hostname, request);
        foreach (var tamper in _tampers)
        {
            RunSafely(tamper, "AutoTamperRequestAfter", () => tamper.AutoTamperRequestAfter(exchange));
        }
    }

    /// <inheritdoc/>
    public CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (_tampers.Count == 0)
        {
            return response;
        }

        var exchange = Exchange.ForResponse(sessionOrdinal, hostname, request, response);
        foreach (var tamper in _tampers)
        {
            RunSafely(tamper, "AutoTamperResponseBefore", () => tamper.AutoTamperResponseBefore(exchange));
        }

        return exchange.ToResponse();
    }

    /// <inheritdoc/>
    public void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (_tampers.Count == 0)
        {
            return;
        }

        var exchange = Exchange.ForResponse(sessionOrdinal, hostname, request, response);
        foreach (var tamper in _tampers)
        {
            RunSafely(tamper, "AutoTamperResponseAfter", () => tamper.AutoTamperResponseAfter(exchange));
        }
    }

    /// <summary>
    /// Runs one extension's hook, isolating a throw to just that extension --
    /// matching real Fiddler's own multi-extension posture (one badly-behaved
    /// loaded extension doesn't take the others, or the connection, down with
    /// it). Deliberately catches <see cref="Exception"/> broadly rather than a
    /// narrower type: unlike <see cref="Clearinet.Compatibility.FiddlerScript.FiddlerScriptRunner"/>,
    /// which only ever has Jint's own <see cref="FiddlerScriptException"/> to
    /// worry about, an extension is arbitrary compiled .NET code and can
    /// throw literally anything.
    /// </summary>
    private void RunSafely(IAutoTamper tamper, string hookName, Action run)
    {
        try
        {
            run();
        }
        catch (Exception ex)
        {
            _log($"[Extension] {tamper.GetType().FullName}.{hookName} threw: {ex.Message}");
        }
    }
}
