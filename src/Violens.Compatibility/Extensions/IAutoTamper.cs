using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>IAutoTamper</c>, member for member
/// (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp). Takes the same
/// <see cref="Exchange"/> FiddlerScript handlers get, so there's one session
/// object model rather than two.
/// </summary>
public interface IAutoTamper : IFiddlerExtension
{
    /// <summary>Fiddler's <c>AutoTamperRequestBefore(Session oSession)</c> -- fires before a request is sent, the same timing as FiddlerScript's own <c>OnBeforeRequest</c>.</summary>
    void AutoTamperRequestBefore(Exchange oSession);

    /// <summary>Fiddler's <c>AutoTamperRequestAfter</c>: the request has been sent.</summary>
    void AutoTamperRequestAfter(Exchange oSession);

    /// <summary>Fiddler's <c>AutoTamperResponseBefore(Session oSession)</c> -- fires before a response is returned to the client, the same timing as FiddlerScript's own <c>OnBeforeResponse</c>.</summary>
    void AutoTamperResponseBefore(Exchange oSession);

    /// <summary>Fiddler's <c>AutoTamperResponseAfter(Session oSession)</c> -- fires once a response has been fully returned to the client. Not wired into the listener yet.</summary>
    void AutoTamperResponseAfter(Exchange oSession);

    /// <summary>
    /// Fiddler's <c>OnBeforeReturningError</c>. Never called: VioLens doesn't
    /// build error responses for extensions to see.
    /// </summary>
    void OnBeforeReturningError(Exchange oSession);
}
