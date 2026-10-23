namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// A <c>[RulesOption("MenuText"[, "SubmenuName"[, isRadio]])]</c> on a
/// boolean field: a checkable Rules menu item.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c> field.</param>
/// <param name="MenuText">The item's text. Access-key <c>&amp;</c>s are left in as typed.</param>
/// <param name="SubmenuName">
/// The submenu, or null. The menu is flat, so this groups radio options and
/// prefixes the label ("Submenu: Option").
/// </param>
/// <param name="IsRadio">Whether checking it unchecks the others in its submenu.</param>
public sealed record RulesMenuOption(string FieldName, string MenuText, string? SubmenuName, bool IsRadio);
