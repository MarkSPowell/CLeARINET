using System.Text;
using System.Text.RegularExpressions;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Scans a script's RAW source (before <see cref="FiddlerScriptPreprocessor"/>
/// strips attribute blocks out entirely -- see that class's own remarks on
/// why it strips rather than reads them) for the Phase B/C/D menu-binding
/// attributes real Fiddler documents publicly: <c>RulesOption</c>,
/// <c>RulesString</c>/<c>RulesStringValue</c>, <c>BindPref</c>,
/// <c>ContextAction</c>, <c>ToolsAction</c>, <c>BindUIColumn</c> -- see the
/// FiddlerScript Compatibility Design doc's Phase B/C/D section for the
/// public documentation (Telerik's own "Customize Menus" and "Add Columns
/// to Web Sessions List" pages, plus a Telerik blog post for
/// <c>BindPref</c>) these argument shapes were drawn from.
///
/// Not a real parser, the same "targeted regex passes" posture
/// <see cref="FiddlerScriptPreprocessor"/> itself takes and for the same
/// reason: every real sample checked against this while scoping the feature
/// declares these attributes in the same flat, single-line-per-attribute
/// shape, stacked directly above the field/method they describe with no
/// blank line or comment in between. A script that formats these
/// differently (an attribute split across multiple lines, a comment
/// wedged between the attribute stack and its declaration) won't be
/// recognized -- the field/method still loads and runs normally either way
/// (this scanner runs independently of, and before,
/// <see cref="FiddlerScriptPreprocessor.Transpile"/>, which still strips
/// the attribute block so the script parses), it just won't get a Rules
/// menu item / column / etc. Silent in that specific case, matching this
/// project's existing "an assembly with no RequiredVersion is silently
/// skipped" precedent for a directive that was never meant to be found
/// rather than one that was found and rejected.
/// </summary>
public static class FiddlerScriptDirectiveScanner
{
    // One `[AttributeName(args)]` on its own line. Deliberately the same
    // shape FiddlerScriptPreprocessor.AttributeBlock strips (see that
    // class's own remarks) -- this scanner runs against the untouched raw
    // source instead of after stripping, so it sees these lines before they
    // disappear.
    private static readonly Regex AttributeLine = new(
        @"^[ \t]*\[(?<name>\w+)\((?<args>[^\]]*)\)\][ \t]*\r?\n",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // One or more consecutive AttributeLine-shaped lines, immediately
    // followed by either a `static var NAME: TYPE` field declaration or a
    // `static function NAME(` method declaration -- the two declaration
    // shapes every attribute here attaches to. A real script commonly
    // stacks several attributes above one declaration (a RulesString field
    // typically has one RulesString plus several RulesStringValue lines
    // above it; a BindPref field can carry a RulesOption too) -- the whole
    // stack is captured as one block and split apart below, rather than
    // trying to match each attribute to its declaration one at a time.
    private static readonly Regex AttributeStackThenDeclaration = new(
        @"(?<attrs>(?:^[ \t]*\[\w+\([^\]]*\)\][ \t]*\r?\n)+)" +
        @"^[ \t]*(?:public\s+|private\s+|protected\s+)?static\s+" +
        @"(?:var\s+(?<field>\w+)\s*:\s*(?<type>[\w.\[\]<>]+)|function\s+(?<method>\w+)\s*\()",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public static FiddlerScriptDirectives Scan(string rawSource)
    {
        var rulesOptions = new List<RulesMenuOption>();
        var rulesStrings = new List<RulesMenuStringOption>();
        var bindPrefs = new List<BindPrefBinding>();
        var contextActions = new List<ContextActionDescriptor>();
        var toolsActions = new List<ToolsActionDescriptor>();
        var uiColumns = new List<UIColumnDescriptor>();

        foreach (Match stack in AttributeStackThenDeclaration.Matches(rawSource))
        {
            var attributes = AttributeLine.Matches(stack.Groups["attrs"].Value)
                .Select(m => (Name: m.Groups["name"].Value, Args: ParseArguments(m.Groups["args"].Value)))
                .ToList();

            if (stack.Groups["field"].Success)
            {
                ScanFieldAttributes(
                    stack.Groups["field"].Value,
                    stack.Groups["type"].Success ? stack.Groups["type"].Value : null,
                    attributes, rulesOptions, rulesStrings, bindPrefs);
            }
            else if (stack.Groups["method"].Success)
            {
                ScanMethodAttributes(stack.Groups["method"].Value, attributes, contextActions, toolsActions, uiColumns);
            }
        }

        return new FiddlerScriptDirectives(rulesOptions, rulesStrings, bindPrefs, contextActions, toolsActions, uiColumns);
    }

    private static void ScanFieldAttributes(
        string fieldName,
        string? fieldType,
        List<(string Name, List<string> Args)> attributes,
        List<RulesMenuOption> rulesOptions,
        List<RulesMenuStringOption> rulesStrings,
        List<BindPrefBinding> bindPrefs)
    {
        var rulesOption = attributes.FirstOrDefault(a => a.Name == "RulesOption");
        if (rulesOption.Name is not null && rulesOption.Args.Count >= 1)
        {
            var submenu = rulesOption.Args.Count >= 2 ? rulesOption.Args[1] : null;
            var isRadio = rulesOption.Args.Count >= 3 && IsTrue(rulesOption.Args[2]);
            rulesOptions.Add(new RulesMenuOption(fieldName, rulesOption.Args[0], submenu, isRadio));
        }

        var rulesString = attributes.FirstOrDefault(a => a.Name == "RulesString");
        if (rulesString.Name is not null && rulesString.Args.Count >= 1)
        {
            // Telerik's own documented example always passes true for the
            // (optional) second argument -- defaulting to radio-exclusive
            // when it's omitted, rather than to independent checkboxes,
            // matches every real sample found while scoping this.
            var isRadio = rulesString.Args.Count < 2 || IsTrue(rulesString.Args[1]);
            var choices = attributes
                .Where(a => a.Name == "RulesStringValue" && a.Args.Count >= 3)
                .Select(a => new RulesStringChoice(a.Args[1], a.Args[2]))
                .ToList();
            rulesStrings.Add(new RulesMenuStringOption(fieldName, rulesString.Args[0], isRadio, choices));
        }

        var bindPref = attributes.FirstOrDefault(a => a.Name == "BindPref");
        if (bindPref.Name is not null && bindPref.Args.Count >= 1)
        {
            var kind = string.Equals(fieldType, "boolean", StringComparison.OrdinalIgnoreCase)
                ? FieldValueKind.Boolean
                : FieldValueKind.String;
            bindPrefs.Add(new BindPrefBinding(fieldName, bindPref.Args[0], kind));
        }
    }

    private static void ScanMethodAttributes(
        string methodName,
        List<(string Name, List<string> Args)> attributes,
        List<ContextActionDescriptor> contextActions,
        List<ToolsActionDescriptor> toolsActions,
        List<UIColumnDescriptor> uiColumns)
    {
        var contextAction = attributes.FirstOrDefault(a => a.Name == "ContextAction");
        if (contextAction.Name is not null && contextAction.Args.Count >= 1)
        {
            contextActions.Add(new ContextActionDescriptor(methodName, contextAction.Args[0]));
        }

        var toolsAction = attributes.FirstOrDefault(a => a.Name == "ToolsAction");
        if (toolsAction.Name is not null && toolsAction.Args.Count >= 1)
        {
            toolsActions.Add(new ToolsActionDescriptor(methodName, toolsAction.Args[0]));
        }

        var bindColumn = attributes.FirstOrDefault(a => a.Name == "BindUIColumn");
        if (bindColumn.Name is not null && bindColumn.Args.Count >= 1)
        {
            uiColumns.Add(BuildUIColumnDescriptor(methodName, bindColumn.Args));
        }
    }

    /// <summary>
    /// Tells apart real Fiddler's four documented <c>BindUIColumn</c>
    /// overloads from the flat argument list this scanner already split
    /// out: a two-argument call's second argument is
    /// <see cref="UIColumnDescriptor.SortNumerically"/> if it parses as a
    /// boolean, otherwise <see cref="UIColumnDescriptor.Width"/> if it
    /// parses as an integer -- exactly the two two-argument overloads
    /// Telerik's own documentation lists, told apart the only way possible
    /// once the argument is already down to plain text: by what it looks
    /// like.
    /// </summary>
    private static UIColumnDescriptor BuildUIColumnDescriptor(string methodName, List<string> args)
    {
        var columnTitle = args[0];
        int? width = null;
        int? displayOrder = null;
        var sortNumerically = false;

        if (args.Count == 2)
        {
            if (bool.TryParse(args[1], out var asBool))
            {
                sortNumerically = asBool;
            }
            else if (int.TryParse(args[1], out var asWidth))
            {
                width = asWidth;
            }
        }
        else if (args.Count >= 3)
        {
            if (int.TryParse(args[1], out var asWidth))
            {
                width = asWidth;
            }

            if (int.TryParse(args[2], out var asOrder))
            {
                displayOrder = asOrder;
            }
        }

        return new UIColumnDescriptor(methodName, columnTitle, width, displayOrder, sortNumerically);
    }

    private static bool IsTrue(string arg) => bool.TryParse(arg, out var value) && value;

    /// <summary>
    /// Splits a raw <c>"a", "b", true</c> attribute-argument string into its
    /// individual literals, unquoting/unescaping any that were quoted
    /// strings and trimming everything else -- a small, deliberately
    /// non-general literal splitter (comma-separated, quote-aware, nothing
    /// more) matching this whole scanner's "targeted regex passes, not a
    /// real parser" posture (see this class's own remarks).
    ///
    /// Known gap, not silently swallowed: a quoted string argument
    /// containing a literal <c>)</c> would already have broken
    /// <see cref="AttributeLine"/>'s own capture before this method ever
    /// runs (that regex stops at the first <c>]</c>, with no awareness of
    /// quoting at all) -- not observed in any real menu-text argument
    /// fetched while scoping this (every documented example is a short,
    /// paren-free label), flagged here rather than hidden.
    /// </summary>
    private static List<string> ParseArguments(string rawArgs)
    {
        var result = new List<string>();
        var i = 0;
        while (i < rawArgs.Length)
        {
            while (i < rawArgs.Length && (char.IsWhiteSpace(rawArgs[i]) || rawArgs[i] == ','))
            {
                i++;
            }

            if (i >= rawArgs.Length)
            {
                break;
            }

            if (rawArgs[i] == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < rawArgs.Length && rawArgs[i] != '"')
                {
                    if (rawArgs[i] == '\\' && i + 1 < rawArgs.Length)
                    {
                        i++;
                    }

                    sb.Append(rawArgs[i]);
                    i++;
                }

                i++; // Skip the closing quote.
                result.Add(sb.ToString());
            }
            else
            {
                var start = i;
                while (i < rawArgs.Length && rawArgs[i] != ',')
                {
                    i++;
                }

                result.Add(rawArgs[start..i].Trim());
            }
        }

        return result;
    }
}
