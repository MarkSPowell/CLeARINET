namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// One <c>[RulesOption("MenuText"[, "SubmenuName"[, isRadio]])]</c>-declared
/// checkable Rules-menu item, scanned from a script's own source by
/// <see cref="FiddlerScriptDirectiveScanner"/> -- see that class's own
/// remarks for exactly what shape of script text this is built from, and
/// the FiddlerScript Compatibility Design doc's Phase B section for the
/// public-documentation source this attribute's argument order was drawn
/// from (Telerik's own "Customize Menus" page).
///
/// Always backs a <c>public static var FIELDNAME: boolean</c> declaration --
/// see <see cref="FiddlerScriptHost.GetRulesOptionValue"/>/
/// <see cref="FiddlerScriptHost.SetRulesOptionValue"/> for how the desktop
/// UI reads/writes the field this describes.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c>-class static field this option is declared on -- what a get/set call needs, never shown to the person directly.</param>
/// <param name="MenuText">The option's own display text, e.g. <c>"Spoof IE &amp;6.0"</c> -- the <c>&amp;</c> marks an access-key letter in real Fiddler; this pass doesn't wire access keys, so it's left in the text as-is (a harmless, visible <c>&amp;</c> rather than a silently-dropped one -- see the design doc's own remarks on this cut).</param>
/// <param name="SubmenuName">
/// The submenu this option groups under, if any -- <see langword="null"/>
/// for a bare top-level Rules-menu item. Real Fiddler nests these under an
/// actual flyout submenu; this pass renders every Rules-menu entry as a
/// single flat list instead (see <see cref="FiddlerScriptDirectives"/>'s
/// own remarks on why), using this purely to group radio-exclusive options
/// together and to prefix a readable "Submenu: Option" label.
/// </param>
/// <param name="IsRadio">Whether this option is mutually exclusive with every other <see cref="RulesMenuOption"/> sharing the same submenu -- checking one unchecks the rest of its group, matching real Fiddler's own radio-button submenu behavior.</param>
public sealed record RulesMenuOption(string FieldName, string MenuText, string? SubmenuName, bool IsRadio);
