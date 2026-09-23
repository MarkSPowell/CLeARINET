namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// One <c>[ToolsAction("MenuText")]</c>-declared method, scanned from a
/// script's own source -- takes no parameters at all (unlike
/// <see cref="ContextActionDescriptor"/>, which operates on a selection);
/// see <see cref="FiddlerScriptRunner.InvokeToolsAction"/> for how the
/// desktop UI's Tools menu calls it.
/// </summary>
/// <param name="MethodName">The <c>Handlers</c>-class static method this menu item calls.</param>
/// <param name="MenuText">The Tools-menu item's own display text.</param>
public sealed record ToolsActionDescriptor(string MethodName, string MenuText);
