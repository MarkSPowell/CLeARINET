using Violens.Compatibility.Extensions;
using Violens.Compatibility.FiddlerScript;

namespace Violens.SampleExtension;

/// <summary>
/// An original <see cref="IAutoTamper3"/>. The four request/response hooks
/// run on real traffic; the other three aren't called by the app yet and
/// are covered by SampleAutoTamperTests.
/// </summary>
public sealed class SampleAutoTamper : IAutoTamper3
{
    /// <summary>The header both Before hooks set -- the same value either side uses, so "did this fire" is a single, easy thing to grep for in either the request or the response.</summary>
    public const string MarkerHeaderName = "X-Violens-SampleExtension";

    public bool Loaded { get; private set; }

    public bool Unloaded { get; private set; }

    public int RequestAfterCount { get; private set; }

    public int ResponseAfterCount { get; private set; }

    public int BeforeReturningErrorCount { get; private set; }

    public int PeekResponseHeadersCount { get; private set; }

    public int PeekRequestHeadersCount { get; private set; }

    /// <summary>Sets <see cref="Loaded"/> and writes a console line, so it's clear the extension loaded.</summary>
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
    /// Adds a request header that reaches the server. Send a request to
    /// https://httpbin.org/post and look for it in the echo.
    /// </summary>
    public void AutoTamperRequestBefore(Exchange oSession)
    {
        oSession.oRequest.headers[MarkerHeaderName] = "AutoTamperRequestBefore-ran";
    }

    /// <summary>Logs and counts; the request has already been sent.</summary>
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

    /// <summary>Not called by the app yet; covered by SampleAutoTamperTests.</summary>
    public void OnBeforeReturningError(Exchange oSession)
    {
        BeforeReturningErrorCount++;
        Console.WriteLine($"[SampleExtension] OnBeforeReturningError: #{oSession.id} {oSession.url}");
    }

    /// <summary>Not called by the app yet.</summary>
    public void OnPeekAtResponseHeaders(Exchange oSession)
    {
        PeekResponseHeadersCount++;
        Console.WriteLine($"[SampleExtension] OnPeekAtResponseHeaders: #{oSession.id} {oSession.responseCode}");
    }

    /// <summary>Not called by the app yet.</summary>
    public void OnPeekAtRequestHeaders(Exchange oSession)
    {
        PeekRequestHeadersCount++;
        Console.WriteLine($"[SampleExtension] OnPeekAtRequestHeaders: #{oSession.id} {oSession.url}");
    }
}
