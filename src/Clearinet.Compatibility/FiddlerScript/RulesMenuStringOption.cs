namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// One named choice inside a <see cref="RulesMenuStringOption"/> submenu --
/// scanned from a <c>[RulesStringValue(index, "Name", "Value")]</c>
/// attribute (Telerik's own documented "more compact syntax" alternative to
/// several separate <see cref="RulesMenuOption"/> booleans -- see the design
/// doc's Phase B section).
/// </summary>
/// <param name="Name">The choice's own display text.</param>
/// <param name="Value">What the bound string field is set to when this choice is picked.</param>
public sealed record RulesStringChoice(string Name, string Value);

/// <summary>
/// One <c>[RulesString("SubmenuName"[, isRadio])]</c>-declared submenu of
/// string choices, together with every <see cref="RulesStringChoice"/>
/// attribute stacked alongside it -- see
/// <see cref="FiddlerScriptDirectiveScanner"/>'s own remarks for the exact
/// script shape this is scanned from.
///
/// <see cref="RulesStringChoice.Value"/>-index ordering (the numeric first
/// argument real Fiddler's own <c>RulesStringValue(index, ...)</c> takes) is
/// deliberately NOT honored here -- <see cref="Choices"/> is built in the
/// order the attributes appear in the script's own source instead. A real
/// script reordering its <c>RulesStringValue</c> declarations to change menu
/// order, rather than relying on source order matching intended order, is an
/// edge case not observed in anything fetched while scoping this; flagged
/// here rather than silently doing something different from what the index
/// argument implies.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c>-class static <c>String</c> field this submenu's choices are written to.</param>
/// <param name="SubmenuName">The submenu's own display text.</param>
/// <param name="IsRadio">Whether picking a choice deselects every other choice in the same submenu -- true for every real-world <c>RulesString</c> sample seen while scoping this (a non-exclusive string submenu isn't a documented Fiddler pattern), but still scanned rather than assumed, in case a script declares otherwise.</param>
/// <param name="Choices">Every <c>RulesStringValue</c> stacked alongside this submenu's own <c>RulesString</c> attribute, in source order.</param>
public sealed record RulesMenuStringOption(
    string FieldName, string SubmenuName, bool IsRadio, IReadOnlyList<RulesStringChoice> Choices);
