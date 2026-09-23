using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// End-to-end tests running small, original FiddlerScript-style snippets
/// (written for this test suite, not copied from any real
/// <c>CustomRules.js</c> or the cookbook samples referenced from
/// <see cref="FiddlerScriptHost"/>'s own remarks) through the real
/// preprocess-then-Jint pipeline. Deliberately styled the same way real
/// scripts are (typed <c>var</c> declarations, <c>class Handlers</c>,
/// <c>static function</c> methods) so a passing test here is real evidence
/// the pipeline handles that shape, not just the preprocessor's own
/// isolated string output.
/// </summary>
public class FiddlerScriptHostTests
{
    private static CapturedRequest SampleRequest(string method = "GET", string target = "/widgets") =>
        new(method, target, "HTTP/1.1", [("Host", "api.example.com")], []);

    [Fact]
    public void OnBeforeRequest_CanAddARequestHeader()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.oRequest.headers["X-Injected-By-Script"] = "yep";
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        host.InvokeOnBeforeRequest(exchange);

        Assert.Equal("yep", exchange.oRequest.headers["X-Injected-By-Script"]);
    }

    [Fact]
    public void OnBeforeRequest_CanReadAUrlAndSetAUiFlag()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    if (oSession.uriContains("widgets")) {
                        oSession["ui-color"] = "orange";
                    }
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest(target: "/widgets/42"));

        host.InvokeOnBeforeRequest(exchange);

        Assert.Equal("orange", exchange["ui-color"]);
    }

    [Fact]
    public void OnBeforeResponse_CanRewriteTheResponseBody()
    {
        const string script = """
            class Handlers {
                static function OnBeforeResponse(oSession: Session) {
                    if (oSession.HostnameIs("api.example.com")) {
                        oSession.utilSetResponseBody("mocked");
                    }
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(),
            new CapturedResponse("HTTP/1.1", 200, "OK", [], "original"u8.ToArray()));

        host.InvokeOnBeforeResponse(exchange);

        Assert.Equal("mocked", System.Text.Encoding.UTF8.GetString(exchange.responseBodyBytes));
    }

    [Fact]
    public void AScriptWithTypedFieldsAndAttributesStillLoadsAndRunsItsHandler()
    {
        const string script = """
            class Handlers {
                [
                RulesOption("&Example Option")
                ]
                public static var m_ExampleOption: boolean = false;

                static var lastSeenMethod: String = "";

                static function OnBeforeRequest(oSession: Session) {
                    oSession["ui-customcolumn"] = oSession.oRequest.headers["Host"];
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());
        exchange.oRequest.headers["Host"] = "api.example.com";

        host.InvokeOnBeforeRequest(exchange);

        Assert.Equal("api.example.com", exchange["ui-customcolumn"]);
    }

    [Fact]
    public void AScriptUsingClrInteropCanConstructAStringBuilder()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    var sb: System.Text.StringBuilder = new System.Text.StringBuilder();
                    sb.Append("built-by-");
                    sb.Append("clr-interop");
                    oSession["ui-customcolumn"] = sb.ToString();
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        host.InvokeOnBeforeRequest(exchange);

        Assert.Equal("built-by-clr-interop", exchange["ui-customcolumn"]);
    }

    [Fact]
    public void HasHandler_IsFalseForAHandlerTheScriptDoesNotDefine()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                }
            }
            """;
        var host = new FiddlerScriptHost(script);

        Assert.True(host.HasHandler("OnBeforeRequest"));
        Assert.False(host.HasHandler("OnBeforeResponse"));
    }

    [Fact]
    public void InvokingAnUndefinedHandlerIsANoOpRatherThanAnError()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(),
            new CapturedResponse("HTTP/1.1", 200, "OK", [], []));

        var ex = Record.Exception(() => host.InvokeOnBeforeResponse(exchange));

        Assert.Null(ex);
    }

    [Fact]
    public void AScriptSyntaxErrorSurfacesAsAFiddlerScriptException()
    {
        const string brokenScript = "class Handlers { static function OnBeforeRequest(oSession: Session) { ";

        Assert.Throws<FiddlerScriptException>(() => new FiddlerScriptHost(brokenScript));
    }

    [Fact]
    public void ARuntimeErrorInAHandlerSurfacesAsAFiddlerScriptException()
    {
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    oSession.thisMemberDoesNotExist.explode();
                }
            }
            """;
        var host = new FiddlerScriptHost(script);
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        Assert.Throws<FiddlerScriptException>(() => host.InvokeOnBeforeRequest(exchange));
    }

    [Fact]
    public void AlertRoutesThroughTheHostsLogCallbackInsteadOfBlockingOnAUi()
    {
        var messages = new List<string>();
        const string script = """
            class Handlers {
                static function OnBeforeRequest(oSession: Session) {
                    FiddlerObject.alert("hello from script");
                }
            }
            """;
        var host = new FiddlerScriptHost(script, new AppObject(log: messages.Add));
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        host.InvokeOnBeforeRequest(exchange);

        Assert.Contains(messages, m => m.Contains("hello from script"));
    }
}
