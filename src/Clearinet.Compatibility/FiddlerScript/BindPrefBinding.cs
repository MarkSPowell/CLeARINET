namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>Which .NET type a <see cref="BindPrefBinding"/>'s field coerces through -- there's no reflection-based type discovery from a Jint value alone, so this is read off the field's own <c>: boolean</c>/<c>: String</c> declaration at scan time (see <see cref="FiddlerScriptDirectiveScanner"/>). Anything else scans as <see cref="String"/> -- the safer, information-preserving default for a type this pass doesn't specifically recognize.</summary>
public enum FieldValueKind
{
    Boolean,
    String,
}

/// <summary>
/// One <c>[BindPref("prefName")]</c>-declared field, scanned from a script's
/// own source -- see the FiddlerScript Compatibility Design doc's Phase B
/// section (drawn from Telerik's own blog post on FiddlerScript, the only
/// public source found describing this attribute) for what real Fiddler
/// documents: the field's value is reloaded from a stored preference when
/// the script loads, and the preference is updated when the script unloads;
/// a preference name containing "Ephemeral" is kept only in memory for the
/// running process (surviving a script reload, not a CLeARINET restart)
/// rather than written to disk at all.
///
/// <see cref="FiddlerScriptRunner"/>'s own actual behavior is a documented,
/// simpler variant of that: the persisted value loads into the field once,
/// at script-load time (see <see cref="FiddlerScriptRunner.LoadFromSource"/>),
/// and a NEW value is written back to <see cref="FiddlerScriptPreferenceStore"/>
/// immediately whenever <see cref="FiddlerScriptRunner.SetRulesOptionValue"/>/
/// <see cref="FiddlerScriptRunner.SetRulesStringValue"/> is called through
/// the Rules-menu UI -- i.e. round-tripping only actually works end-to-end
/// for a field that's ALSO a <see cref="RulesMenuOption"/>/
/// <see cref="RulesMenuStringOption"/>. A field carrying <c>[BindPref]</c>
/// alone, written to only from arbitrary handler code deep inside a script
/// (real Fiddler's own <c>bpResponseURI</c> cookbook example is exactly
/// this shape), still gets its persisted value loaded in at script-load
/// time, but this pass does not intercept or persist further in-script
/// writes to it -- there's no generic way to observe an arbitrary Jint
/// field assignment without instrumenting every property access, which
/// this pass doesn't attempt. A real, honest scope cut, flagged here rather
/// than silently only half-implemented.
/// </summary>
/// <param name="FieldName">The <c>Handlers</c>-class static field this preference is bound to.</param>
/// <param name="PrefName">The preference's own key, e.g. <c>"clearinet.spoofIE6"</c> or <c>"fiddlerscript.ephemeral.bpResponseURI"</c> -- see <see cref="FiddlerScriptPreferenceStore"/>'s own remarks on the "Ephemeral" convention.</param>
/// <param name="Kind">Which .NET type to coerce the persisted string value through when applying it back into the field.</param>
public sealed record BindPrefBinding(string FieldName, string PrefName, FieldValueKind Kind);
