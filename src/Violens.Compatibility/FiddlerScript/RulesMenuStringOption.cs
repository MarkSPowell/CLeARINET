namespace Violens.Compatibility.FiddlerScript;

/// <summary>One <c>[RulesStringValue(index, "Name", "Value")]</c> choice.</summary>
/// <param name="Name">The choice's text.</param>
/// <param name="Value">What the field is set to.</param>
public sealed record RulesStringChoice(string Name, string Value);

/// <summary>
/// A <c>[RulesString("SubmenuName"[, isRadio])]</c> on a string field, with
/// its choices. Choices are in source order; the index argument is ignored.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c> string field.</param>
/// <param name="SubmenuName">The submenu's text.</param>
/// <param name="IsRadio">Whether picking a choice clears the others.</param>
/// <param name="Choices">The choices, in source order.</param>
public sealed record RulesMenuStringOption(
    string FieldName, string SubmenuName, bool IsRadio, IReadOnlyList<RulesStringChoice> Choices);
