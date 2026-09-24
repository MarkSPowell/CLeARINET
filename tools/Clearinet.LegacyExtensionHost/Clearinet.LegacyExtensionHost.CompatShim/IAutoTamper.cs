namespace Clearinet.CompatShim;

/// <summary>
/// Signatures reproduced from fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp
/// (public docs) -- same source and shape as
/// <c>Clearinet.Compatibility.Extensions.IAutoTamper</c>. Only plain
/// <c>IAutoTamper</c> is implemented here (not <c>IAutoTamper2</c>/<c>3</c>):
/// none of the five real extensions inspected for this shim reference the
/// <c>2</c>/<c>3</c> variants, so they're left out rather than built
/// speculatively -- extend using the same public-doc signatures already
/// recorded in the sibling source-compatible interface if a future sample
/// needs them.
/// </summary>
public interface IAutoTamper : IFiddlerExtension
{
    void AutoTamperRequestBefore(Session oSession);
    void AutoTamperRequestAfter(Session oSession);
    void AutoTamperResponseBefore(Session oSession);
    void AutoTamperResponseAfter(Session oSession);
    void OnBeforeReturningError(Session oSession);
}
