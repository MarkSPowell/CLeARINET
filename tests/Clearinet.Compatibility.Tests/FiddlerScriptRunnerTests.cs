using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Scripting;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Exercises <see cref="FiddlerScriptRunner"/> through its
/// <see cref="IFiddlerScriptRunner"/> surface specifically -- the same
/// narrow contract <c>InterceptingProxyListener</c> actually calls -- rather
/// than reaching past it into <see cref="FiddlerScriptHost"/>/<see cref="Exchange"/>
/// directly, which <see cref="FiddlerScriptHostTests"/> already covers.
/// Every script snippet here is original, written for this test suite.
/// </summary>
public class FiddlerScriptRunnerTests
{
    private static CapturedRequest SampleRequest(string method = "GET", string target = "/widgets") =>
        new(method, target, "HTTP/1.1", [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse() =>
        new("HTTP/1.1", 200, "OK", [], "original"u8.ToArray());

    [Fact]
    public void BeforeAnythingIsLoaded_HasFlagsAreFalseAndRunningIsANoOp()
    {
        IFiddlerScriptRunner runner = new FiddlerScriptRunner();

        Assert.False(runner.HasOnBeforeRequest);
        Assert.False(runner.HasOnBeforeResponse);

        var request = SampleRequest();
        var requestResult = runner.RunOnBeforeRequest(1, "api.example.com", request);
        Assert.Same(request, requestResult.Request);

        var response = SampleResponse();
        var responseResult = runner.RunOnBeforeResponse(1, "api.example.com", request, response);
        Assert.Same(response, responseResult.Response);
    }

    [Fact]
    public void LoadFromSource_SetsHasFlagsFromWhateverHandlersTheScriptDefines()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                }
            }
            """;
        var runner = new FiddlerScriptRunner();

        runner.LoadFromSource(script);

        Assert.True(runner.IsLoaded);
        Assert.Null(runner.LoadError);
        Assert.True(runner.HasOnBeforeRequest);
        Assert.False(runner.HasOnBeforeResponse);
    }

    [Fact]
    public void RunOnBeforeRequest_ReturnsTheEditedRequestThroughTheInterface()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.oRequest.headers["X-Runner-Test"] = "edited";
                }
            }
            """;
        IFiddlerScriptRunner runner = LoadedRunner(script);

        var result = runner.RunOnBeforeRequest(1, "api.example.com", SampleRequest());

        Assert.Equal("edited", result.Request.Headers.Single(h => h.Name == "X-Runner-Test").Value);
    }

    [Fact]
    public void RunOnBeforeResponse_ReturnsTheEditedResponseThroughTheInterface()
    {
        const string script = """
            class Handlers {
                static function OnBeforeResponse(oSession: Session) {
                    oSession.utilSetResponseBody("replaced-by-runner");
                }
            }
            """;
        IFiddlerScriptRunner runner = LoadedRunner(script);

        var result = runner.RunOnBeforeResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        Assert.Equal("replaced-by-runner", System.Text.Encoding.UTF8.GetString(result.Response.Body));
    }

    [Fact]
    public void RunOnBeforeRequest_PassesTheSessionOrdinalThroughAsOSessionId()
    {
        // oSession["ui-customcolumn"] only round-trips through Exchange
        // itself, which FiddlerScriptRequestResult doesn't carry -- so this
        // asserts via the request body instead, which does round-trip
        // through the interface's own result type.
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.requestBodyBytes = System.Text.Encoding.UTF8.GetBytes("id=" + oSession.id.toString());
                }
            }
            """;
        IFiddlerScriptRunner runner = LoadedRunner(script);

        var result = runner.RunOnBeforeRequest(42, "api.example.com", SampleRequest());

        Assert.Equal("id=42", System.Text.Encoding.UTF8.GetString(result.Request.Body));
    }

    [Fact]
    public void ARuntimeErrorInAHandler_FallsBackToTheUneditedValueRatherThanThrowing()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.thisMemberDoesNotExist.explode();
                }
            }
            """;
        IFiddlerScriptRunner runner = LoadedRunner(script);
        var request = SampleRequest();

        var result = runner.RunOnBeforeRequest(1, "api.example.com", request);

        Assert.Same(request, result.Request);
    }

    [Fact]
    public void LoadFromSource_ASyntaxError_SetsLoadErrorRatherThanThrowing()
    {
        const string brokenScript = "class Handlers { static function OnBeforeRequest(oSession: Session) { ";
        var runner = new FiddlerScriptRunner();

        var ex = Record.Exception(() => runner.LoadFromSource(brokenScript));

        Assert.Null(ex);
        Assert.False(runner.IsLoaded);
        Assert.NotNull(runner.LoadError);
    }

    [Fact]
    public void LoadFromSource_AFailedReload_KeepsTheLastGoodScriptRunning()
    {
        const string goodScript = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.oRequest.headers["X-Runner-Test"] = "still-good";
                }
            }
            """;
        var runner = new FiddlerScriptRunner();
        runner.LoadFromSource(goodScript);

        const string brokenScript = "class Handlers { static function OnBeforeRequest(oSession: Session) { ";
        runner.LoadFromSource(brokenScript);

        Assert.NotNull(runner.LoadError);
        Assert.True(runner.IsLoaded);
        Assert.True(runner.HasOnBeforeRequest);

        var result = runner.RunOnBeforeRequest(1, "api.example.com", SampleRequest());
        Assert.Equal("still-good", result.Request.Headers.Single(h => h.Name == "X-Runner-Test").Value);
    }

    [Fact]
    public void LoadFromFile_ReadsTheGivenPathAndRemembersItForReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clearinet-runner-test-{Guid.NewGuid():N}.js");
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.oRequest.headers["X-Runner-Test"] = "from-file";
                }
            }
            """;
        File.WriteAllText(path, script);

        try
        {
            var runner = new FiddlerScriptRunner();
            runner.LoadFromFile(path);

            Assert.Equal(path, runner.LoadedPath);
            Assert.True(runner.HasOnBeforeRequest);

            var result = runner.RunOnBeforeRequest(1, "api.example.com", SampleRequest());
            Assert.Equal("from-file", result.Request.Headers.Single(h => h.Name == "X-Runner-Test").Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reload_ReReadsTheFileAndPicksUpAnEditMadeSinceTheLastLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"clearinet-runner-test-{Guid.NewGuid():N}.js");
        const string originalScript = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.oRequest.headers["X-Runner-Test"] = "original";
                }
            }
            """;
        File.WriteAllText(path, originalScript);

        try
        {
            var runner = new FiddlerScriptRunner();
            runner.LoadFromFile(path);

            const string editedScript = """
                class Handlers {
                    static function OnBeforeRequest(oSession: Session) {
                        oSession.oRequest.headers["X-Runner-Test"] = "edited-then-reloaded";
                    }
                }
                """;
            File.WriteAllText(path, editedScript);
            runner.Reload();

            var result = runner.RunOnBeforeRequest(1, "api.example.com", SampleRequest());
            Assert.Equal("edited-then-reloaded", result.Request.Headers.Single(h => h.Name == "X-Runner-Test").Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reload_WithNothingEverLoadedFromAFile_IsANoOp()
    {
        var runner = new FiddlerScriptRunner();

        var ex = Record.Exception(runner.Reload);

        Assert.Null(ex);
        Assert.False(runner.IsLoaded);
    }

    private static FiddlerScriptRunner LoadedRunner(string script)
    {
        var runner = new FiddlerScriptRunner();
        runner.LoadFromSource(script);
        return runner;
    }

    private static string NewTempPrefsPath() =>
        Path.Combine(Path.GetTempPath(), $"clearinet-runner-prefs-test-{Guid.NewGuid():N}.json");

    private static Session SampleSession(int id = 1) => new(
        id, "api.example.com", DateTimeOffset.Now, SampleRequest(), SampleResponse());

    [Fact]
    public void Directives_IsEmptyBeforeAnythingIsLoaded()
    {
        var runner = new FiddlerScriptRunner();

        Assert.Same(FiddlerScriptDirectives.Empty, runner.Directives);
    }

    [Fact]
    public void Directives_ReflectsWhateverTheLoadedScriptDeclares()
    {
        const string script = """
            class Handlers {
                [RulesOption("Example &Toggle")]
                public static var m_ExampleToggle: boolean = false;
            }
            """;
        var runner = LoadedRunner(script);

        var option = Assert.Single(runner.Directives.RulesMenuOptions);
        Assert.Equal("m_ExampleToggle", option.FieldName);
    }

    [Fact]
    public void SetThenGetRulesOptionValue_RoundTripsThroughTheRunningScript()
    {
        const string script = """
            class Handlers {
                [RulesOption("Example &Toggle")]
                public static var m_ExampleToggle: boolean = false;
            }
            """;
        var runner = LoadedRunner(script);

        Assert.False(runner.GetRulesOptionValue("m_ExampleToggle"));
        runner.SetRulesOptionValue("m_ExampleToggle", true);
        Assert.True(runner.GetRulesOptionValue("m_ExampleToggle"));
    }

    [Fact]
    public void SetThenGetRulesStringValue_RoundTripsThroughTheRunningScript()
    {
        const string script = """
            class Handlers {
                [RulesString("&Example Choices", true)]
                [RulesStringValue(0, "First", "first-value")]
                [RulesStringValue(1, "Second", "second-value")]
                public static var m_ExampleChoice: String = "first-value";
            }
            """;
        var runner = LoadedRunner(script);

        Assert.Equal("first-value", runner.GetRulesStringValue("m_ExampleChoice"));
        runner.SetRulesStringValue("m_ExampleChoice", "second-value");
        Assert.Equal("second-value", runner.GetRulesStringValue("m_ExampleChoice"));
    }

    [Fact]
    public void ABindPrefBooleanField_LoadsItsPersistedValueOnLoad()
    {
        const string script = """
            class Handlers {
                [BindPref("clearinet.test.example")]
                [RulesOption("Example &Toggle")]
                public static var m_ExampleToggle: boolean = false;
            }
            """;
        var prefsPath = NewTempPrefsPath();
        try
        {
            new FiddlerScriptPreferenceStore(prefsPath).Set("clearinet.test.example", "true");

            var runner = new FiddlerScriptRunner(preferenceStore: new FiddlerScriptPreferenceStore(prefsPath));
            runner.LoadFromSource(script);

            // The field's own initializer says false; the persisted
            // preference (set above, before this runner ever loaded
            // anything) is what should have won.
            Assert.True(runner.GetRulesOptionValue("m_ExampleToggle"));
        }
        finally
        {
            File.Delete(prefsPath);
        }
    }

    [Fact]
    public void SettingABindPrefBooleanField_PersistsAcrossARunnerRestart()
    {
        const string script = """
            class Handlers {
                [BindPref("clearinet.test.example")]
                [RulesOption("Example &Toggle")]
                public static var m_ExampleToggle: boolean = false;
            }
            """;
        var prefsPath = NewTempPrefsPath();
        try
        {
            var firstRunner = new FiddlerScriptRunner(preferenceStore: new FiddlerScriptPreferenceStore(prefsPath));
            firstRunner.LoadFromSource(script);
            firstRunner.SetRulesOptionValue("m_ExampleToggle", true);

            // A brand-new runner, over a brand-new store instance pointed
            // at the same file -- simulates CLeARINET restarting.
            var secondRunner = new FiddlerScriptRunner(preferenceStore: new FiddlerScriptPreferenceStore(prefsPath));
            secondRunner.LoadFromSource(script);

            Assert.True(secondRunner.GetRulesOptionValue("m_ExampleToggle"));
        }
        finally
        {
            File.Delete(prefsPath);
        }
    }

    [Fact]
    public void InvokeContextAction_CallsTheScriptMethodWithTheGivenSessions()
    {
        const string script = """
            class Handlers {
                [ContextAction("Copy &Example URL")]
                static function DoCopyExampleUrl(oSessions: Session[]) {
                    oSessions[0].oRequest.headers["X-Context-Action-Ran"] = "yes";
                }
            }
            """;
        var runner = LoadedRunner(script);
        var session = SampleSession();

        var ex = Record.Exception(() => runner.InvokeContextAction("DoCopyExampleUrl", [session]));

        Assert.Null(ex);
    }

    [Fact]
    public void InvokeContextAction_ARuntimeErrorIsLoggedRatherThanThrown()
    {
        const string script = """
            class Handlers {
                [ContextAction("Copy &Example URL")]
                static function DoCopyExampleUrl(oSessions: Session[]) {
                    oSessions[0].thisMemberDoesNotExist.explode();
                }
            }
            """;
        var messages = new List<string>();
        var runner = new FiddlerScriptRunner(log: messages.Add);
        runner.LoadFromSource(script);

        var ex = Record.Exception(() => runner.InvokeContextAction("DoCopyExampleUrl", [SampleSession()]));

        Assert.Null(ex);
        Assert.Contains(messages, m => m.Contains("DoCopyExampleUrl"));
    }

    [Fact]
    public void InvokeToolsAction_CallsTheScriptMethod()
    {
        const string script = """
            class Handlers {
                static var s_ExampleCounterResetCount: int = 0;

                [ToolsAction("Reset Example &Counters")]
                static function DoResetExampleCounters() {
                    s_ExampleCounterResetCount = s_ExampleCounterResetCount + 1;
                }
            }
            """;
        var runner = LoadedRunner(script);

        var ex = Record.Exception(() => runner.InvokeToolsAction("DoResetExampleCounters"));

        Assert.Null(ex);
    }

    [Fact]
    public void ComputeUIColumnValue_ReturnsWhateverTheScriptMethodReturns()
    {
        const string script = """
            class Handlers {
                [BindUIColumn("HasExampleCookie")]
                static function ColHasExampleCookie(oS: Session): String {
                    return oS.oRequest.headers["Cookie"] != null ? "yes" : "no";
                }
            }
            """;
        var runner = LoadedRunner(script);
        var session = SampleSession();

        Assert.Equal("no", runner.ComputeUIColumnValue("ColHasExampleCookie", session));
    }

    [Fact]
    public void ComputeUIColumnValue_ARuntimeErrorReturnsAPlaceholderRatherThanThrowing()
    {
        const string script = """
            class Handlers {
                [BindUIColumn("Broken")]
                static function ColBroken(oS: Session): String {
                    return oS.thisMemberDoesNotExist.explode();
                }
            }
            """;
        var runner = LoadedRunner(script);

        var result = runner.ComputeUIColumnValue("ColBroken", SampleSession());

        Assert.Equal("(error)", result);
    }
}
