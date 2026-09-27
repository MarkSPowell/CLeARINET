namespace Clearinet.CompatShim;

/// <summary>
/// Fiddler's <c>IFiddlerExtension</c>: the base every extension implements.
/// <see cref="OnLoad"/> runs once, on the UI thread, after the app has
/// started and every extension has been constructed; <see cref="OnBeforeUnload"/>
/// runs as the app shuts down.
///
/// This and the <c>IAutoTamper</c> family below take this layer's own
/// Fiddler-shaped <see cref="Session"/>, so a ported extension compiles
/// unchanged. The host runs them through
/// <c>Clearinet.Compatibility.Extensions.ShimAutoTamperSet</c>, which keeps
/// one <see cref="Session"/> per request across all of its hooks. Written
/// from Telerik's published FiddlerCore API reference.
/// </summary>
public interface IFiddlerExtension
{
    void OnLoad();

    void OnBeforeUnload();
}

/// <summary>
/// Fiddler's <c>IAutoTamper</c>: called for every request and response.
/// <list type="bullet">
/// <item><see cref="AutoTamperRequestBefore"/>: before the request is sent.
/// Changes are sent. Calling <see cref="Session.utilCreateResponseAndBypassServer"/>
/// here answers the request without contacting the server.</item>
/// <item><see cref="AutoTamperRequestAfter"/>: after it's sent. Changes aren't sent.</item>
/// <item><see cref="AutoTamperResponseBefore"/>: before the response is
/// returned to the client. Changes are returned.</item>
/// <item><see cref="AutoTamperResponseAfter"/>: after it's returned.</item>
/// <item><see cref="OnBeforeReturningError"/>: before CLeARINET returns an
/// error response of its own. Not called yet: the proxy doesn't generate
/// error responses today.</item>
/// </list>
/// </summary>
public interface IAutoTamper : IFiddlerExtension
{
    void AutoTamperRequestBefore(Session oSession);

    void AutoTamperRequestAfter(Session oSession);

    void AutoTamperResponseBefore(Session oSession);

    void AutoTamperResponseAfter(Session oSession);

    void OnBeforeReturningError(Session oSession);
}

/// <summary>Fiddler's <c>IAutoTamper2</c>: adds a look at the response's status line and headers before its body is read.</summary>
public interface IAutoTamper2 : IAutoTamper
{
    void OnPeekAtResponseHeaders(Session oSession);
}

/// <summary>Fiddler's <c>IAutoTamper3</c>: adds a look at the request's request line and headers before its body is read.</summary>
public interface IAutoTamper3 : IAutoTamper2
{
    void OnPeekAtRequestHeaders(Session oSession);
}
