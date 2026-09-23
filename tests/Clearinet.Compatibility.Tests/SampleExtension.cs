using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// An original class written for this test suite -- not a port of SAZClipboard.dll or
/// any other real extension's source, including the SAZClipboard source Mark found a
/// Creative Commons copy of at fiddler.wikidot.com/sazclipboard (CC BY-SA 3.0, not
/// obviously compatible with this project's own MIT license -- see the Extension
/// Compatibility Design doc). This class doesn't depend on having read that page at
/// all.
///
/// Deliberately implements every method across the whole documented
/// IFiddlerExtension/IAutoTamper/IAutoTamper2/IAutoTamper3/IHandleExecAction family in
/// one class -- a "kitchen sink" shape no real extension is likely to need, but a
/// direct way to exercise every documented member at least once in one place. Each
/// method does something small and independently observable (appends to
/// <see cref="CallLog"/>, and/or edits the <see cref="Exchange"/> it's given) rather
/// than nothing, so a test asserting on it is actually proving the member works
/// end-to-end through <see cref="Exchange"/>, not just that the interfaces compile.
/// </summary>
public sealed class SampleExtension : IAutoTamper3, IHandleExecAction
{
    /// <summary>Every call this instance has received, in order -- lets a test confirm both "did this fire" and "did these fire in the right order" without a mocking framework.</summary>
    public List<string> CallLog { get; } = [];

    public void OnLoad() => CallLog.Add(nameof(OnLoad));

    public void OnBeforeUnload() => CallLog.Add(nameof(OnBeforeUnload));

    public void AutoTamperRequestBefore(Exchange oSession)
    {
        CallLog.Add(nameof(AutoTamperRequestBefore));
        oSession.oRequest.headers["X-Sample-Extension"] = nameof(AutoTamperRequestBefore);
    }

    public void AutoTamperRequestAfter(Exchange oSession)
    {
        CallLog.Add(nameof(AutoTamperRequestAfter));
        oSession["x-sample-request-after"] = "1";
    }

    public void AutoTamperResponseBefore(Exchange oSession)
    {
        CallLog.Add(nameof(AutoTamperResponseBefore));
        oSession.utilSetResponseBody("rewritten-by-sample-extension");
    }

    public void AutoTamperResponseAfter(Exchange oSession)
    {
        CallLog.Add(nameof(AutoTamperResponseAfter));
        oSession["x-sample-response-after"] = "1";
    }

    public void OnBeforeReturningError(Exchange oSession)
    {
        CallLog.Add(nameof(OnBeforeReturningError));
        oSession["ui-color"] = "red";
    }

    public void OnPeekAtResponseHeaders(Exchange oSession)
    {
        CallLog.Add(nameof(OnPeekAtResponseHeaders));
        oSession["x-sample-peeked-response-headers"] = "1";
    }

    public void OnPeekAtRequestHeaders(Exchange oSession)
    {
        CallLog.Add(nameof(OnPeekAtRequestHeaders));
        oSession["x-sample-peeked-request-headers"] = "1";
    }

    /// <summary>Recognizes exactly one made-up command, "sample.ping" -- see <see cref="IHandleExecAction.OnExecAction"/>'s own remarks on why the true/false meaning here is a flagged assumption, not confirmed Fiddler behavior.</summary>
    public bool OnExecAction(string sCommand)
    {
        CallLog.Add($"{nameof(OnExecAction)}({sCommand})");
        return string.Equals(sCommand, "sample.ping", StringComparison.OrdinalIgnoreCase);
    }
}
