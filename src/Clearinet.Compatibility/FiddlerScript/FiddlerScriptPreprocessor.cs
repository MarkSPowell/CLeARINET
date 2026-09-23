using System.Text.RegularExpressions;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Rewrites a real <c>CustomRules.js</c> file's JScript.NET-specific
/// syntax into plain ECMAScript Jint can parse. Not a real parser -- a
/// handful of targeted regex passes, each scoped as narrowly as possible
/// to avoid corrupting ordinary JS it shouldn't touch (a ternary's
/// <c>cond ? a : b</c>, an object literal's <c>{key: value}</c>, a
/// <c>switch</c> label's <c>case X:</c>). See the FiddlerScript
/// Compatibility Design doc for why a real parser wasn't the starting
/// point: the four transforms below cover what every sample script fetched
/// from <c>ericlaw1979/Clearinet</c>'s <c>SampleRules.js</c> and
/// fiddlerbook.com's cookbook actually needs, and a hand-rolled JScript.NET
/// grammar is a much bigger undertaking than this first pass warranted.
///
/// Known gap, not silently swallowed: a parameter-position type annotation
/// whose "type" is actually an object-literal value that happens to look
/// like a bare identifier (<c>{a: 1, name: someIdentifier}</c>) can be
/// mis-stripped, since <see cref="StripParameterTypeAnnotations"/> can't
/// tell that apart from a real <c>(name: Type)</c> annotation using regex
/// alone. Not observed in any real sample checked against this so far;
/// flagged here rather than hidden so it's the first thing to check if a
/// real script behaves unexpectedly after preprocessing.
/// </summary>
public static class FiddlerScriptPreprocessor
{
    // "import System.Text;" / "import Clearinet;" -- JScript.NET namespace
    // imports. Nothing to translate them to: Jint's CLR interop (enabled in
    // FiddlerScriptHost) resolves a fully-qualified name like
    // `System.Text.StringBuilder` directly, so an import isn't needed for
    // the scripts that use it, and isn't valid ES module syntax for Jint to
    // parse either way -- dropping the line entirely is correct, not just
    // convenient.
    private static readonly Regex ImportLine = new(@"^\s*import\s+[\w.]+\s*;\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    // A `[...]` attribute block on its own line(s) before a declaration --
    // real FiddlerScript's `[RulesOption("...")]`/`[BindPref("...")]`/etc.
    // JScript.NET attribute syntax, borrowed from C#. Not valid JS at all.
    // This phase doesn't act on what these attributes declare (see the
    // Compatibility Design doc's Phase B/C), but a script that has them
    // alongside real OnBeforeRequest/OnBeforeResponse logic still needs to
    // parse, so they're stripped rather than left to break the whole file.
    // Deliberately only matches a bracket group that is the only thing on
    // its line(s) -- an actual JS array literal spanning a statement
    // wouldn't match this shape.
    private static readonly Regex AttributeBlock = new(@"^[ \t]*\[[^\]]*\][ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);

    // `public static var NAME: TYPE = VALUE;` (or any subset of the
    // `public`/`static`/`var` keywords) -- a menu-bound field declaration.
    // Rewritten to a bare ES2022 static class field (`static NAME = VALUE;`)
    // so it at least parses; nothing reads these yet (same Phase B/C note
    // as above).
    private static readonly Regex StaticFieldDeclaration = new(
        @"\b(?:public\s+|private\s+|protected\s+)?static\s+var\s+(\w+)\s*:\s*[\w.\[\]<>]+",
        RegexOptions.Compiled);

    // `static function Name(...)` -- JScript.NET's own class-method syntax.
    // ES2022 classes support `static Name(...) { }` directly (no `function`
    // keyword), so this only needs to drop that one keyword to become
    // parseable -- Handlers' overall `class Handlers { ... }` shape is
    // already valid ES6+ class syntax as-is.
    private static readonly Regex StaticMethodKeyword = new(@"\bstatic\s+function\s+", RegexOptions.Compiled);

    // A bare (non-static) `function Name(...)` -- FiddlerScript files
    // sometimes declare free helper functions outside the Handlers class
    // too (the cookbook samples do this for shared logic between rules).
    // Plain JS `function` declarations already parse as-is; only the type
    // annotations inside them need stripping, handled separately below.

    // Return-type annotation: `): Type {` -- specific enough (closing paren,
    // colon, bare dotted type, opening brace) not to collide with a
    // ternary or object literal, which don't have this exact shape.
    private static readonly Regex ReturnTypeAnnotation = new(@"\)\s*:\s*[\w.\[\]<>]+\s*(?=\{)", RegexOptions.Compiled);

    // `var NAME: TYPE` (not already caught by StaticFieldDeclaration above,
    // which is scoped to the `static var` field-declaration case) -- a
    // plain local variable declaration inside a function body.
    private static readonly Regex VarDeclarationAnnotation = new(@"\bvar\s+(\w+)\s*:\s*[\w.\[\]<>]+", RegexOptions.Compiled);

    // Parameter-position annotation: `(NAME: Type` or `, NAME: Type` where
    // Type is a bare dotted identifier (optionally `[]`-suffixed) directly
    // followed by `,` or `)`. Scoped this tightly specifically to avoid an
    // object literal's `{key: value}` -- see this class's own remarks on
    // the one known gap that scoping doesn't close.
    private static readonly Regex ParameterTypeAnnotation = new(
        @"(?<=[(,])(\s*)(\w+)\s*:\s*[A-Za-z_][\w.]*(?:\[\])?\s*(?=[,)])",
        RegexOptions.Compiled);

    public static string Transpile(string jScriptDotNetSource)
    {
        var text = jScriptDotNetSource;

        text = ImportLine.Replace(text, string.Empty);
        text = AttributeBlock.Replace(text, string.Empty);
        text = StaticFieldDeclaration.Replace(text, "static $1");
        text = StaticMethodKeyword.Replace(text, "static ");
        text = ReturnTypeAnnotation.Replace(text, ")");
        text = VarDeclarationAnnotation.Replace(text, "var $1");
        text = ParameterTypeAnnotation.Replace(text, "$1$2");

        return text;
    }
}
