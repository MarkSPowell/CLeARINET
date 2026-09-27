using Clearinet.Compatibility.FiddlerScript;
using Xunit;

namespace Clearinet.Compatibility.Tests;

public class FiddlerScriptPreprocessorTests
{
    [Fact]
    public void Transpile_DropsImportLines()
    {
        var result = FiddlerScriptPreprocessor.Transpile("import System;\nimport System.Text;\nvar x = 1;");

        Assert.DoesNotContain("import", result);
        Assert.Contains("var x = 1;", result);
    }

    [Fact]
    public void Transpile_StripsAMultiLineAttributeBlock()
    {
        const string source = "[\nRulesOption(\"&Auto Auth\")\n]\npublic static var m_Foo: boolean = false;";

        var result = FiddlerScriptPreprocessor.Transpile(source);

        Assert.DoesNotContain("RulesOption", result);
        Assert.DoesNotContain("[", result);
        Assert.Contains("static m_Foo = false;", result);
    }

    [Fact]
    public void Transpile_StripsASingleLineAttributeBlock()
    {
        const string source = "[ToolsAction(\"Reset Script\")]\nstatic function ResetScript() {\n}";

        var result = FiddlerScriptPreprocessor.Transpile(source);

        Assert.DoesNotContain("ToolsAction", result);
        Assert.Contains("static ResetScript() {", result);
    }

    [Fact]
    public void Transpile_ConvertsStaticFunctionToAnEs6StaticMethod()
    {
        var result = FiddlerScriptPreprocessor.Transpile("static function OnBeforeRequest(oSession: Session) {\n}");

        Assert.Contains("static OnBeforeRequest(oSession) {", result);
        Assert.DoesNotContain("function", result);
        Assert.DoesNotContain(":", result);
    }

    [Fact]
    public void Transpile_StripsAReturnTypeAnnotation()
    {
        var result = FiddlerScriptPreprocessor.Transpile("function FillMethodColumn(oEx: Exchange): String {\n}");

        Assert.Contains("function FillMethodColumn(oEx){", result);
    }

    [Fact]
    public void Transpile_StripsADottedNamespaceTypedVarDeclaration()
    {
        var result = FiddlerScriptPreprocessor.Transpile(
            "var sbOut: System.Text.StringBuilder = new System.Text.StringBuilder();");

        Assert.Equal("var sbOut = new System.Text.StringBuilder();", result);
    }

    [Fact]
    public void Transpile_StripsMultipleTypedParametersOnOneSignature()
    {
        var result = FiddlerScriptPreprocessor.Transpile(
            "static function OnBeforeResponse(oSession: Session, extra: Boolean) {\n}");

        Assert.Contains("static OnBeforeResponse(oSession, extra) {", result);
    }

    [Fact]
    public void Transpile_LeavesPlainEcmaScriptUntouched()
    {
        const string source = "static OnBeforeRequest(oSession) {\n  if (oSession.HTTPMethodIs(\"GET\")) { oSession[\"ui-color\"] = \"red\"; }\n}";

        var result = FiddlerScriptPreprocessor.Transpile(source);

        Assert.Equal(source, result);
    }
}
