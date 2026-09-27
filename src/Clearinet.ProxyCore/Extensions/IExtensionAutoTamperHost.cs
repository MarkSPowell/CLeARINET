using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Extensions;

/// <summary>
/// The narrow surface <c>InterceptingProxyListener</c> needs to run every
/// loaded .NET extension's <c>IAutoTamper</c> hooks against a real
/// request/response pair -- the same interface-inversion reasoning as
/// <c>Clearinet.ProxyCore.Scripting.IFiddlerScriptRunner</c> (see that
/// interface's own remarks): <c>Clearinet.Compatibility</c> already
/// references <c>Clearinet.ProxyCore</c>, so a type living in
/// <c>ProxyCore</c> can't reference the real
/// <c>Clearinet.Compatibility.Extensions.IAutoTamper</c>-implementing
/// extension instances directly. The composition root
/// (<c>MainWindowViewModel</c>) constructs the real implementation
/// (<c>Clearinet.Compatibility.Extensions.LoadedExtensionSet</c>) and hands
/// it to the listener as this interface.
///
/// Unlike <c>IFiddlerScriptRunner</c> (one script, one engine), any number
/// of extensions can be loaded at once, each independently implementing
/// <c>IAutoTamper</c> -- so every method here runs *every* loaded
/// extension's hook in discovery order, each seeing the previous one's
/// edits, the same way real Fiddler runs multiple loaded extensions'
/// <c>AutoTamperRequestBefore</c> in sequence against one shared
/// <c>oSession</c>. One extension throwing doesn't stop the others or the
/// connection -- see the implementation's own remarks.
///
/// See the .NET Extension Compatibility Design doc's own section on this
/// for the full wiring, including what's deliberately still out of scope:
/// <c>OnBeforeReturningError</c> is not called from anywhere (this proxy
/// doesn't yet synthesize an error response for the real server ever
/// failing to answer -- see that section for why building that is a
/// separate feature, not part of this wiring), and the *Before hooks share
/// the exact same buffer-forcing fork and before/breakpoint ordering
/// <c>IFiddlerScriptRunner</c> already established in Phase A2 -- FiddlerScript's
/// own <c>OnBeforeRequest</c>/<c>OnBeforeResponse</c> run first, then loaded
/// extensions' <c>AutoTamperRequestBefore</c>/<c>AutoTamperResponseBefore</c>,
/// an assumed (not confirmed against real Fiddler) ordering.
/// </summary>
public interface IExtensionAutoTamperHost
{
    /// <summary>Whether ANY loaded extension implements <c>IAutoTamper</c> -- gates the same body-buffering fork <see cref="Scripting.IFiddlerScriptRunner.HasOnBeforeRequest"/> does.</summary>
    bool HasAnyRequestBeforeHandlers { get; }

    /// <summary>Same as <see cref="HasAnyRequestBeforeHandlers"/>, for <c>AutoTamperResponseBefore</c>.</summary>
    bool HasAnyResponseBeforeHandlers { get; }

    /// <summary>Runs every loaded extension's <c>AutoTamperRequestBefore</c> in turn and returns the (possibly repeatedly edited) result.</summary>
    CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>
    /// Runs every loaded extension's <c>AutoTamperRequestAfter</c> -- fire-and-observe
    /// only, matching real Fiddler: by the time this fires the request has
    /// already gone out over the wire, so nothing here can change what was
    /// sent. Safe to call unconditionally (whether or not any extension is
    /// loaded, or defines this hook) -- it internally no-ops rather than
    /// requiring a caller-side <c>Has*</c> check the way the Before hooks
    /// do, since it never needs to force buffering.
    /// </summary>
    void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>Same as <see cref="RunRequestBefore"/>, for <c>AutoTamperResponseBefore</c>.</summary>
    CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);

    /// <summary>Same as <see cref="RunRequestAfter"/>, for <c>AutoTamperResponseAfter</c>.</summary>
    void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);
}
