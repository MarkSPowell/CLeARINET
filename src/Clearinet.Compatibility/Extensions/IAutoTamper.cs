using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Fiddler Classic's <c>IAutoTamper</c> -- a compiled extension's equivalent of
/// FiddlerScript's <c>Handlers.OnBeforeRequest</c>/<c>OnBeforeResponse</c>
/// (see <see cref="Clearinet.Compatibility.FiddlerScript.FiddlerScriptHost"/>), just
/// with two more hook points (the <c>*After</c> pair) FiddlerScript's own two-handler
/// surface doesn't expose. Reproduced member-for-member from
/// https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp -- see
/// <see cref="IFiddlerExtension"/>'s own remarks for the source-vs-binary
/// compatibility distinction this whole namespace is built around.
///
/// Takes <see cref="Exchange"/> -- the exact same session-facing object model
/// FiddlerScript's own <c>Handlers</c> functions already use -- rather than a second,
/// parallel "Session-shaped" type built just for .NET extensions. Deliberate: one
/// object model serves both compatibility layers, so an extension author already
/// familiar with Fiddler's <c>oSession.hostname</c>/<c>oSession.oRequest</c>/etc.
/// finds the exact same names here, and CLeARINET only has one set of
/// request/response mutation plumbing (<see cref="Exchange.ToRequest"/>/
/// <see cref="Exchange.ToResponse"/>) to keep correct, not two independent ones that
/// could drift apart.
/// </summary>
public interface IAutoTamper : IFiddlerExtension
{
    /// <summary>Fiddler's <c>AutoTamperRequestBefore(Session oSession)</c> -- fires before a request is sent, the same timing as FiddlerScript's own <c>OnBeforeRequest</c>.</summary>
    void AutoTamperRequestBefore(Exchange oSession);

    /// <summary>
    /// Fiddler's <c>AutoTamperRequestAfter(Session oSession)</c> -- fires once a
    /// request has been sent upstream. Not wired into
    /// <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c> yet -- see the
    /// Extension Compatibility Design doc's "what's not built yet."
    /// </summary>
    void AutoTamperRequestAfter(Exchange oSession);

    /// <summary>Fiddler's <c>AutoTamperResponseBefore(Session oSession)</c> -- fires before a response is returned to the client, the same timing as FiddlerScript's own <c>OnBeforeResponse</c>.</summary>
    void AutoTamperResponseBefore(Exchange oSession);

    /// <summary>Fiddler's <c>AutoTamperResponseAfter(Session oSession)</c> -- fires once a response has been fully returned to the client. Not wired into the listener yet.</summary>
    void AutoTamperResponseAfter(Exchange oSession);

    /// <summary>
    /// Fiddler's <c>OnBeforeReturningError(Session oSession)</c> -- fires when
    /// Fiddler itself is about to return a synthetic error response (a DNS failure, a
    /// connection refusal) rather than a real server's own response. Not wired into
    /// the listener yet -- there's no synthetic-error-response code path there to
    /// hook into today; <c>InterceptingProxyListener</c> currently just lets a
    /// connection-level exception end the connection (see its own <c>catch</c>
    /// blocks) rather than building a Fiddler-style error <see cref="Exchange"/> for
    /// an extension to react to.
    /// </summary>
    void OnBeforeReturningError(Exchange oSession);
}
