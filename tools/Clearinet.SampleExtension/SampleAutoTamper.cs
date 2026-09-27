using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.SampleExtension;

/// <summary>
/// An original, from-scratch <see cref="IAutoTamper3"/> implementation --
/// not a port of <c>SAZClipboard.dll</c> or any other real Fiddler Classic
/// extension, the same clean-room posture as
/// <c>samples/PhaseA2ValidationRules.js</c>. Exercises every method
/// <see cref="Clearinet.Compatibility.Extensions.IFiddlerExtension"/>/
/// <see cref="IAutoTamper"/>/<see cref="IAutoTamper2"/>/<see cref="IAutoTamper3"/>
/// declare -- the four wired into real proxied traffic today
/// (<see cref="AutoTamperRequestBefore"/>/<see cref="AutoTamperRequestAfter"/>/
/// <see cref="AutoTamperResponseBefore"/>/<see cref="AutoTamperResponseAfter"/>,
/// via <c>LoadedExtensionSet</c>) are each individually observable by eye
/// once this .dll is loaded and CLeARINET is used to browse; the other
/// three (<see cref="OnBeforeReturningError"/>/<see cref="OnPeekAtResponseHeaders"/>/
/// <see cref="OnPeekAtRequestHeaders"/>) aren't called by anything yet (see
/// the .NET Extension Compatibility Design doc's own "what's not built
/// yet") and are exercised only by
/// <c>Clearinet.SampleExtension.Tests.SampleAutoTamperTests</c>, calling
/// each one directly.
/// </summary>
public sealed class SampleAutoTamper : IAutoTamper3
{
    /// <summary>The header both Before hooks set -- the same value either side uses, so "did this fire" is a single, easy thing to grep for in either the request or the response.</summary>
    public const string MarkerHeaderName = "X-Clearinet-SampleExtension";

    public bool Loaded { get; private set; }

    public bool Unloaded { get; private set; }

    public int RequestAfterCount { get; private set; }

    public int ResponseAfterCount { get; private set; }

    public int BeforeReturningErrorCount { get; private set; }

    public int PeekResponseHeadersCount { get; private set; }

    public int PeekRequestHeadersCount { get; private set; }

    /// <summary>
    /// Fired once by <c>ExtensionHost.Load()</c> -- flips <see cref="Loaded"/>
    /// and writes a console line (the same established diagnostic channel
    /// <c>InterceptingProxyListener</c>/<c>FiddlerScriptRunner</c> already
    /// use) so this extension's presence is visible the moment the desktop
    /// app starts, before any traffic has even been proxied.
    /// </summary>
    public void OnLoad()
    {
        Loaded = true;
        Console.WriteLine("[SampleExtension] OnLoad: SampleAutoTamper loaded.");
    }

    /// <summary>Fired once by <c>ExtensionHost.Unload()</c>, from <c>MainWindowViewModel.Dispose()</c>.</summary>
    public void OnBeforeUnload()
    {
        Unloaded = true;
        Console.WriteLine("[SampleExtension] OnBeforeUnload: SampleAutoTamper unloading.");
    }

    /// <summary>
    /// Sets a real request header -- projected back onto the actual wire
    /// request (see <c>LoadedExtensionSet.RunRequestBefore</c>'s own
    /// remarks), so it reaches the real server and is visible in whatever
    /// that server echoes back. Point a request at
    /// https://httpbin.org/post (the same trick
    /// <c>samples/PhaseA2ValidationRules.js</c> uses) and look for this
    /// header in the echoed request.
    /// </summary>
    public void AutoTamperRequestBefore(Exchange oSession)
    {
        oSession.oRequest.headers[MarkerHeaderName] = "AutoTamperRequestBefore-ran";
    }

    /// <summary>
    /// Also wired into real traffic, but fire-and-observe: nothing set on
    /// <paramref name="oSession"/> here is ever projected back onto the
    /// wire (see <c>IExtensionAutoTamperHost.RunRequestAfter</c>'s own
    /// remarks), so the only observable effects are this console line and
    /// the bumped counter -- not a header a browser could ever see.
    /// </summary>
    public void AutoTamperRequestAfter(Exchange oSession)
    {
        RequestAfterCount++;
        Console.WriteLine($"[SampleExtension] AutoTamperRequestAfter: #{oSession.id} {oSession.url}");
    }

    /// <summary>Same as <see cref="AutoTamperRequestBefore"/>, response side -- visible in a browser's own dev tools, or any HTTP client that shows response headers.</summary>
    public void AutoTamperResponseBefore(Exchange oSession)
    {
        oSession.oResponse.headers[MarkerHeaderName] = "AutoTamperResponseBefore-ran";
    }

    /// <summary>Same as <see cref="AutoTamperRequestAfter"/>, response side.</summary>
    public void AutoTamperResponseAfter(Exchange oSession)
    {
        ResponseAfterCount++;
        Console.WriteLine($"[SampleExtension] AutoTamperResponseAfter: #{oSession.id} {oSession.responseCode} {oSession.url}");
    }

    /// <summary>
    /// Not called by anything yet -- <c>InterceptingProxyListener</c> has no
    /// synthetic-error-response code path to hang this off (see the design
    /// doc's own "what's not built yet"). Implemented fully so the
    /// interface contract itself is proven; exercised directly by
    /// <c>SampleAutoTamperTests</c>.
    /// </summary>
    public void OnBeforeReturningError(Exchange oSession)
    {
        BeforeReturningErrorCount++;
        Console.WriteLine($"[SampleExtension] OnBeforeReturningError: #{oSession.id} {oSession.url}");
    }

    /// <summary>Not called by anything yet either -- see <see cref="OnBeforeReturningError"/>'s own remarks.</summary>
    public void OnPeekAtResponseHeaders(Exchange oSession)
    {
        PeekResponseHeadersCount++;
        Console.WriteLine($"[SampleExtension] OnPeekAtResponseHeaders: #{oSession.id} {oSession.responseCode}");
    }

    /// <summary>Not called by anything yet either -- see <see cref="OnBeforeReturningError"/>'s own remarks.</summary>
    public void OnPeekAtRequestHeaders(Exchange oSession)
    {
        PeekRequestHeadersCount++;
        Console.WriteLine($"[SampleExtension] OnPeekAtRequestHeaders: #{oSession.id} {oSession.url}");
    }
}
