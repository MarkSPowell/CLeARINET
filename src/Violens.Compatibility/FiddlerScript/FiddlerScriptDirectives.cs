namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// The menu and column attributes found in one script, parsed into plain
/// descriptors a UI can build from.
/// </summary>
public sealed record FiddlerScriptDirectives(
    IReadOnlyList<RulesMenuOption> RulesMenuOptions,
    IReadOnlyList<RulesMenuStringOption> RulesMenuStringOptions,
    IReadOnlyList<BindPrefBinding> BindPrefBindings,
    IReadOnlyList<ContextActionDescriptor> ContextActions,
    IReadOnlyList<ToolsActionDescriptor> ToolsActions,
    IReadOnlyList<UIColumnDescriptor> UIColumns)
{
    /// <summary>A script with none of these attributes.</summary>
    public static FiddlerScriptDirectives Empty { get; } = new([], [], [], [], [], []);
}
