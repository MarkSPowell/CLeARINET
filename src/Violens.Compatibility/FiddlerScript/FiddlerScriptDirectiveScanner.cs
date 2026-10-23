using System.Text;
using System.Text.RegularExpressions;

namespace Violens.Compatibility.FiddlerScript;

/// <summary>
/// Finds the menu and column attributes Fiddler documents
/// (<c>RulesOption</c>, <c>RulesString</c>/<c>RulesStringValue</c>,
/// <c>BindPref</c>, <c>ContextAction</c>, <c>ToolsAction</c>,
/// <c>BindUIColumn</c>) in a script's raw source, before the preprocessor
/// strips them. Regex-based: attributes must sit one per line directly
/// above their declaration, as in every sample checked. Others are ignored,
/// and the script still runs.
/// </summary>
public static class FiddlerScriptDirectiveScanner
{
    // One [AttributeName(args)] on its own line, the shape the preprocessor
    // strips.
    private static readonly Regex AttributeLine = new(
        @"^[ \t]*\[(?<name>\w+)\((?<args>[^\]]*)\)\][ \t]*\r?\n",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // A stack of attribute lines followed by the `static var NAME: TYPE` or
    // `static function NAME(` they belong to.
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
    /// Picks the <c>BindUIColumn</c> overload: with two arguments, a boolean
    /// second argument is <see cref="UIColumnDescriptor.SortNumerically"/> and
    /// a number is <see cref="UIColumnDescriptor.Width"/>.
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
    /// Splits <c>"a", "b", true</c> into literals, unquoting strings. A quoted
    /// <c>]</c> isn't supported (AttributeLine stops there).
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
