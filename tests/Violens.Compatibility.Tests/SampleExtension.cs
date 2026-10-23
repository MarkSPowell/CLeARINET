using Violens.Compatibility.Extensions;
using Violens.Compatibility.FiddlerScript;

namespace Violens.Compatibility.Tests;

/// <summary>
/// An original test extension (not a port of any real one) implementing
/// every IFiddlerExtension, IAutoTamper(2/3) and IHandleExecAction member.
/// Each logs to <see cref="CallLog"/> and/or edits its
/// <see cref="Exchange"/>, so tests can check it really ran.
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

    /// <summary>Handles one made-up command, "sample.ping".</summary>
    public bool OnExecAction(string sCommand)
    {
        CallLog.Add($"{nameof(OnExecAction)}({sCommand})");
        return string.Equals(sCommand, "sample.ping", StringComparison.OrdinalIgnoreCase);
    }
}
