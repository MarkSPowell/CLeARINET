namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Everything <see cref="FiddlerScriptDirectiveScanner"/> found declared in
/// one script -- the Phase B/C/D menu-binding attributes real Fiddler
/// supports (<c>RulesOption</c>/<c>RulesString</c>/<c>BindPref</c>/
/// <c>ContextAction</c>/<c>ToolsAction</c>/<c>BindUIColumn</c>), each folded
/// into a plain, already-parsed descriptor a UI layer can build menus/
/// columns from without touching Jint or regex at all -- see
/// <see cref="FiddlerScriptHost.Directives"/> for where this actually lives
/// once a script is loaded.
///
/// Deliberately flat, not hierarchical: real Fiddler nests
/// <see cref="RulesMenuOption"/>/<see cref="RulesMenuStringOption"/> entries
/// under an actual Rules-menu flyout submenu keyed by their own submenu
/// name. This pass doesn't build that nested structure -- see
/// <c>MainWindow.axaml</c>'s own remarks on why a single flat
/// <c>MenuItem.ItemsSource</c> binding was chosen over a recursive
/// hierarchical template -- so the UI layer groups these by submenu name
/// itself, for a "Submenu: Option" label rather than a real flyout.
/// </summary>
public sealed record FiddlerScriptDirectives(
    IReadOnlyList<RulesMenuOption> RulesMenuOptions,
    IReadOnlyList<RulesMenuStringOption> RulesMenuStringOptions,
    IReadOnlyList<BindPrefBinding> BindPrefBindings,
    IReadOnlyList<ContextActionDescriptor> ContextActions,
    IReadOnlyList<ToolsActionDescriptor> ToolsActions,
    IReadOnlyList<UIColumnDescriptor> UIColumns)
{
    /// <summary>What a script with none of these attributes at all scans to -- every real script from before Phase B/C/D existed, in particular.</summary>
    public static FiddlerScriptDirectives Empty { get; } = new([], [], [], [], [], []);
}
