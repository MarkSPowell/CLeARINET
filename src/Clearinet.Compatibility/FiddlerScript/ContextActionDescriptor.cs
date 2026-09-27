namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// One <c>[ContextAction("MenuText")]</c>-declared method, scanned from a
/// script's own source -- real Fiddler's version operates on every
/// currently-selected session at once (a <c>Session[]</c> parameter); this
/// project's session grid only supports single selection today (see
/// <c>MainWindowViewModel.SelectedSessionRow</c>), so
/// <see cref="FiddlerScriptRunner.InvokeContextAction"/> scopes this down to
/// the one currently-selected session -- a real, flagged simplification,
/// not a fidelity goal, and revisited if/when the grid grows multi-select.
/// </summary>
/// <param name="MethodName">The <c>Handlers</c>-class static method this menu item calls.</param>
/// <param name="MenuText">The context-menu item's own display text.</param>
public sealed record ContextActionDescriptor(string MethodName, string MenuText);
