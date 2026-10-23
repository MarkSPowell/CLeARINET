namespace Violens.CompatShim;

/// <summary>
/// Fiddler's <c>IHandleExecAction</c>, used by ContentBlock. True is taken
/// to mean "handled", as in the in-process version.
/// </summary>
public interface IHandleExecAction
{
    bool OnExecAction(string sCommand);
}
