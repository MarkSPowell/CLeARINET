using Violens.Compatibility.FiddlerScript;
using Violens.ProxyCore.Extensions;
using Violens.ProxyCore.Http;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Runs a fixed list of <see cref="IAutoTamper"/> extensions. Each call
/// builds one <see cref="Exchange"/> and runs every extension on it in turn,
/// as Fiddler does, so each sees the previous one's edits. Loading is
/// <see cref="ExtensionHost"/>'s job, so tests can pass fakes here.
/// </summary>
public sealed class LoadedExtensionSet : IExtensionAutoTamperHost
{
    private readonly IReadOnlyList<IAutoTamper> _tampers;
    private readonly Action<string> _log;

    /// <param name="tampers">The extensions, in load order.</param>
    /// <param name="log">Where a hook that throws is logged.</param>
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

        // Built from the request as actually sent.
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
    /// Runs one hook, so one extension throwing doesn't stop the others or the
    /// connection. Catches everything: extensions can throw anything.
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
