namespace Violens.Compatibility.FiddlerScript;

/// <summary>The field's type, from its <c>: boolean</c> or <c>: String</c> annotation; anything else is treated as a string.</summary>
public enum FieldValueKind
{
    Boolean,
    String,
}

/// <summary>
/// A <c>[BindPref("prefName")]</c> field. Its saved value is loaded when the
/// script loads, and saved when changed from the Rules menu. Changes the
/// script makes itself aren't saved, since Jint field writes can't be
/// observed.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c> field.</param>
/// <param name="PrefName">The preference name; see <see cref="FiddlerScriptPreferenceStore"/> for "Ephemeral" names.</param>
/// <param name="Kind">The type to convert the saved value to.</param>
public sealed record BindPrefBinding(string FieldName, string PrefName, FieldValueKind Kind);
