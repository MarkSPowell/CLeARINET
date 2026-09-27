namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Fiddler Classic's <c>IHandleExecAction</c> -- a standalone interface (not part of
/// the <see cref="IFiddlerExtension"/>/<see cref="IAutoTamper"/> chain, per the public
/// docs -- a class is free to implement both, as <c>SampleExtension</c> in the test
/// suite does) an extension implements to add its own commands to Fiddler's QuickExec
/// box. CLeARINET has no QuickExec-equivalent UI yet, so this interface can be
/// implemented and exercised on its own terms but has nothing to actually dispatch a
/// typed command into today -- a future UI surface, not this pass's scope.
/// </summary>
public interface IHandleExecAction
{
    /// <summary>
    /// Fiddler's <c>OnExecAction(string sCommand)</c>. The public docs give the
    /// signature but not a spelled-out return-value contract. By analogy with how a
    /// chain of independent handlers is normally wired (and consistent with
    /// QuickExec plausibly supporting more than one extension's commands at once),
    /// this project treats <see langword="true"/> as "this extension recognized and
    /// handled the command" and <see langword="false"/> as "not mine -- let something
    /// else look at it." That's a reasonable, explicitly flagged assumption, not a
    /// confirmed Fiddler behavior -- worth revisiting if a real QuickExec-equivalent
    /// UI is ever built here.
    /// </summary>
    bool OnExecAction(string sCommand);
}
