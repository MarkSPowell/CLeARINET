namespace Violens.CompatShim;

/// <summary>
/// Fiddler's <c>IAutoTamper</c>, from the public docs
/// (fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp). The 2 and 3
/// variants are left out: none of the samples use them.
/// </summary>
public interface IAutoTamper : IFiddlerExtension
{
    void AutoTamperRequestBefore(Session oSession);
    void AutoTamperRequestAfter(Session oSession);
    void AutoTamperResponseBefore(Session oSession);
    void AutoTamperResponseAfter(Session oSession);
    void OnBeforeReturningError(Session oSession);
}
