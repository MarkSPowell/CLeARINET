namespace Violens.Compatibility.FiddlerScript;

/// <summary>A <c>[ToolsAction("MenuText")]</c> method, which takes no arguments.</summary>
/// <param name="MethodName">The <c>Handlers</c> method.</param>
/// <param name="MenuText">The button's text.</param>
public sealed record ToolsActionDescriptor(string MethodName, string MenuText);
