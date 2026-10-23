namespace Violens.Compatibility.Extensions;

/// <summary>
/// Fiddler's <c>IHandleExecAction</c>: adds commands to QuickExec. Separate
/// from <see cref="IFiddlerExtension"/>; a class may implement both.
/// </summary>
public interface IHandleExecAction
{
    /// <summary>
    /// Fiddler's <c>OnExecAction</c>. True means this extension handled the
    /// command; false lets the next one try. Fiddler doesn't document the
    /// return value, so that's our reading of it.
    /// </summary>
    bool OnExecAction(string sCommand);
}
