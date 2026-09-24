namespace Clearinet.CompatShim;

/// <summary>
/// Signature reproduced from fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp
/// (public docs) -- same shape as
/// <c>Clearinet.Compatibility.Extensions.IHandleExecAction</c>, including
/// that doc's own flagged, unconfirmed assumption: the public docs give the
/// signature but not a spelled-out return-value contract, so <c>true</c> is
/// treated as "handled, stop looking" here too, for consistency.
/// Referenced by <c>ContentBlock</c> among the five samples inspected for
/// this shim.
/// </summary>
public interface IHandleExecAction
{
    bool OnExecAction(string sCommand);
}
