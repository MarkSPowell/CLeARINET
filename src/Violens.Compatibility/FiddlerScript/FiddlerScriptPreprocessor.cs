using System.Text.RegularExpressions;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// Rewrites JScript.NET-only syntax in a <c>CustomRules.js</c> into
/// ECMAScript Jint can parse, with narrow regex passes that leave ternaries,
/// object literals and <c>case</c> labels alone. Enough for Clearinet's
/// SampleRules.js and the fiddlerbook.com cookbook. Known gap: an object
/// literal value that's a bare identifier (<c>{a: 1, name: x}</c>) can be
/// mistaken for a parameter type annotation.
/// </summary>
public static class FiddlerScriptPreprocessor
{
    // JScript.NET imports. Jint resolves full .NET names without them, and
    // they aren't valid here, so the line is dropped.
    private static readonly Regex ImportLine = new(@"^\s*import\s+[\w.]+\s*;\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    // Attribute blocks ([RulesOption(...)] and so on) on their own lines.
    // Not valid JS, so stripped; the scanner reads them first. Only a bracket
    // group alone on its line(s) matches, never an array literal.
    private static readonly Regex AttributeBlock = new(@"^[ \t]*\[[^\]]*\][ \t]*\r?\n", RegexOptions.Multiline | RegexOptions.Compiled);

    // `public static var NAME: TYPE = VALUE;` becomes `static NAME = VALUE;`.
    private static readonly Regex StaticFieldDeclaration = new(
        @"\b(?:public\s+|private\s+|protected\s+)?static\s+var\s+(\w+)\s*:\s*[\w.\[\]<>]+",
        RegexOptions.Compiled);

    // `static function Name(` becomes the class method `static Name(`.
    private static readonly Regex StaticMethodKeyword = new(@"\bstatic\s+function\s+", RegexOptions.Compiled);

    // Free functions outside Handlers parse as they are; only their type
    // annotations need stripping, below.

    // Return-type annotation: `): Type {` -- specific enough (closing paren,
    // colon, bare dotted type, opening brace) not to collide with a
    // ternary or object literal, which don't have this exact shape.
    private static readonly Regex ReturnTypeAnnotation = new(@"\)\s*:\s*[\w.\[\]<>]+\s*(?=\{)", RegexOptions.Compiled);

    // `var NAME: TYPE` (not already caught by StaticFieldDeclaration above,
    // which is scoped to the `static var` field-declaration case) -- a
    // plain local variable declaration inside a function body.
    private static readonly Regex VarDeclarationAnnotation = new(@"\bvar\s+(\w+)\s*:\s*[\w.\[\]<>]+", RegexOptions.Compiled);

    // `(NAME: Type` or `, NAME: Type` followed by `,` or `)`, kept narrow to
    // avoid object literals.
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
