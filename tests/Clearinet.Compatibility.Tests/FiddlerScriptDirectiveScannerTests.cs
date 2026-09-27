using Clearinet.Compatibility.FiddlerScript;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Pure string-in, data-out tests against <see cref="FiddlerScriptDirectiveScanner"/>
/// directly -- no Jint, no <see cref="FiddlerScriptHost"/>, the same
/// "test the transform in isolation" posture <c>FiddlerScriptPreprocessorTests</c>
/// already takes for the sibling regex pass this one runs alongside. Every
/// snippet here is original, written for this test suite -- not copied from
/// any real <c>CustomRules.js</c> or cookbook sample, matching this
/// project's clean-room policy.
/// </summary>
public class FiddlerScriptDirectiveScannerTests
{
    [Fact]
    public void AScriptWithNoAttributesAtAllScansToEmpty()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                }
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        Assert.Empty(directives.RulesMenuOptions);
        Assert.Empty(directives.RulesMenuStringOptions);
        Assert.Empty(directives.BindPrefBindings);
        Assert.Empty(directives.ContextActions);
        Assert.Empty(directives.ToolsActions);
        Assert.Empty(directives.UIColumns);
    }

    [Fact]
    public void RulesOptionWithNoSubmenuScansAsAStandaloneNonRadioOption()
    {
        const string script = """
            class Handlers {
                [RulesOption("Spoof &Example Header")]
                public static var m_SpoofExample: boolean = false;
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var option = Assert.Single(directives.RulesMenuOptions);
        Assert.Equal("m_SpoofExample", option.FieldName);
        Assert.Equal("Spoof &Example Header", option.MenuText);
        Assert.Null(option.SubmenuName);
        Assert.False(option.IsRadio);
    }

    [Fact]
    public void RulesOptionWithASubmenuAndRadioFlagScansBoth()
    {
        const string script = """
            class Handlers {
                [RulesOption("Version &One", "Example Group", true)]
                public static var m_VersionOne: boolean = false;

                [RulesOption("Version &Two", "Example Group", true)]
                public static var m_VersionTwo: boolean = true;
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        Assert.Equal(2, directives.RulesMenuOptions.Count);
        Assert.All(directives.RulesMenuOptions, o => Assert.Equal("Example Group", o.SubmenuName));
        Assert.All(directives.RulesMenuOptions, o => Assert.True(o.IsRadio));
        Assert.Contains(directives.RulesMenuOptions, o => o.FieldName == "m_VersionOne");
        Assert.Contains(directives.RulesMenuOptions, o => o.FieldName == "m_VersionTwo");
    }

    [Fact]
    public void RulesStringWithStackedRulesStringValueAttributesScansAllChoicesInOrder()
    {
        const string script = """
            class Handlers {
                [RulesString("&Response Delay", true)]
                [RulesStringValue(0, "None", "0")]
                [RulesStringValue(1, "One Second", "1000")]
                [RulesStringValue(2, "Five Seconds", "5000")]
                public static var m_DelayMs: String = "0";
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var option = Assert.Single(directives.RulesMenuStringOptions);
        Assert.Equal("m_DelayMs", option.FieldName);
        Assert.Equal("&Response Delay", option.SubmenuName);
        Assert.True(option.IsRadio);
        Assert.Equal(3, option.Choices.Count);
        Assert.Equal(["None", "One Second", "Five Seconds"], option.Choices.Select(c => c.Name));
        Assert.Equal(["0", "1000", "5000"], option.Choices.Select(c => c.Value));
    }

    [Fact]
    public void BindPrefOnABooleanFieldScansWithBooleanKind()
    {
        const string script = """
            class Handlers {
                [BindPref("clearinet.example.spoofExample")]
                [RulesOption("Spoof &Example Header")]
                public static var m_SpoofExample: boolean = false;
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var binding = Assert.Single(directives.BindPrefBindings);
        Assert.Equal("m_SpoofExample", binding.FieldName);
        Assert.Equal("clearinet.example.spoofExample", binding.PrefName);
        Assert.Equal(FieldValueKind.Boolean, binding.Kind);

        // Also still scanned as a RulesOption -- one field, two attributes,
        // two descriptors (see BindPrefBinding's own remarks on this being
        // the shape that actually round-trips end to end).
        Assert.Single(directives.RulesMenuOptions);
    }

    [Fact]
    public void BindPrefAloneOnAStringFieldScansWithStringKindAndNoRulesMenuEntry()
    {
        const string script = """
            class Handlers {
                [BindPref("fiddlerscript.ephemeral.bpExampleUri")]
                public static var bpExampleUri: String = null;
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var binding = Assert.Single(directives.BindPrefBindings);
        Assert.Equal("bpExampleUri", binding.FieldName);
        Assert.Equal("fiddlerscript.ephemeral.bpExampleUri", binding.PrefName);
        Assert.Equal(FieldValueKind.String, binding.Kind);
        Assert.Empty(directives.RulesMenuOptions);
        Assert.Empty(directives.RulesMenuStringOptions);
    }

    [Fact]
    public void ContextActionAndToolsActionScanFromTheirOwnStaticMethods()
    {
        const string script = """
            class Handlers {
                [ContextAction("Copy &Example URL")]
                static function DoCopyExampleUrl(oSessions: Session[]) {
                }

                [ToolsAction("Reset Example &Counters")]
                static function DoResetExampleCounters() {
                }
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var contextAction = Assert.Single(directives.ContextActions);
        Assert.Equal("DoCopyExampleUrl", contextAction.MethodName);
        Assert.Equal("Copy &Example URL", contextAction.MenuText);

        var toolsAction = Assert.Single(directives.ToolsActions);
        Assert.Equal("DoResetExampleCounters", toolsAction.MethodName);
        Assert.Equal("Reset Example &Counters", toolsAction.MenuText);
    }

    [Theory]
    [InlineData("""[BindUIColumn("HasExampleCookie")]""", null, null, false)]
    [InlineData("""[BindUIColumn("HasExampleCookie", true)]""", null, null, true)]
    [InlineData("""[BindUIColumn("HasExampleCookie", 90)]""", 90, null, false)]
    [InlineData("""[BindUIColumn("HasExampleCookie", 90, 3)]""", 90, 3, false)]
    public void BindUIColumnTellsItsFourOverloadsApart(string attributeLine, int? expectedWidth, int? expectedOrder, bool expectedSortNumerically)
    {
        var script = $$"""
            class Handlers {
                {{attributeLine}}
                static function ColHasExampleCookie(oS: Session): String {
                    return "";
                }
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        var column = Assert.Single(directives.UIColumns);
        Assert.Equal("ColHasExampleCookie", column.MethodName);
        Assert.Equal("HasExampleCookie", column.ColumnTitle);
        Assert.Equal(expectedWidth, column.Width);
        Assert.Equal(expectedOrder, column.DisplayOrder);
        Assert.Equal(expectedSortNumerically, column.SortNumerically);
    }

    [Fact]
    public void AnAttributeNotImmediatelyAboveADeclarationIsNotScanned()
    {
        // A comment (or blank line) wedged between the attribute and its
        // declaration is a documented gap -- see this scanner's own
        // remarks. The script still loads and runs fine either way
        // (FiddlerScriptPreprocessor strips the attribute regardless); it
        // just doesn't get a Rules-menu entry.
        const string script = """
            class Handlers {
                [RulesOption("Spoof &Example Header")]
                // A comment landing here breaks the scan.
                public static var m_SpoofExample: boolean = false;
            }
            """;

        var directives = FiddlerScriptDirectiveScanner.Scan(script);

        Assert.Empty(directives.RulesMenuOptions);
    }
}
