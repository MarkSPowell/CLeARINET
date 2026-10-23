namespace Violens.Compatibility.FiddlerScript;

/// <summary>A <c>[ContextAction("MenuText")]</c> method, run on the selected session.</summary>
/// <param name="MethodName">The <c>Handlers</c> method.</param>
/// <param name="MenuText">The menu item's text.</param>
public sealed record ContextActionDescriptor(string MethodName, string MenuText);
