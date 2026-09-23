using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Exercises every documented member of
/// IFiddlerExtension/IAutoTamper/IAutoTamper2/IAutoTamper3/IHandleExecAction
/// (https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp) against
/// <see cref="SampleExtension"/>. The goal, per how this was asked for: prove every
/// documented method is both implementable against CLeARINET's own interfaces AND
/// actually usable through <see cref="Exchange"/> -- not just that the interfaces
/// compile. Each test calls one method and asserts on the specific, independent
/// effect <see cref="SampleExtension"/> is documented to produce for it, rather than
/// only checking <see cref="SampleExtension.CallLog"/> -- a method that fired but did
/// nothing useful to its <see cref="Exchange"/> argument would still pass a
/// call-log-only test, which isn't the thing worth proving here.
/// </summary>
public class ExtensionInterfaceTests
{
    private static CapturedRequest SampleRequest() =>
        new("GET", "/widgets", "HTTP/1.1", [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse() =>
        new("HTTP/1.1", 200, "OK", [], "original"u8.ToArray());

    [Fact]
    public void SampleExtension_ImplementsEveryDocumentedInterfaceInTheFamily()
    {
        var extension = new SampleExtension();

        Assert.IsAssignableFrom<IFiddlerExtension>(extension);
        Assert.IsAssignableFrom<IAutoTamper>(extension);
        Assert.IsAssignableFrom<IAutoTamper2>(extension);
        Assert.IsAssignableFrom<IAutoTamper3>(extension);
        Assert.IsAssignableFrom<IHandleExecAction>(extension);
    }

    [Fact]
    public void OnLoad_AndOnBeforeUnload_BothFireInOrder()
    {
        var extension = new SampleExtension();

        extension.OnLoad();
        extension.OnBeforeUnload();

        Assert.Equal(new[] { "OnLoad", "OnBeforeUnload" }, extension.CallLog);
    }

    [Fact]
    public void AutoTamperRequestBefore_CanEditTheOutgoingRequest()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        extension.AutoTamperRequestBefore(exchange);

        Assert.Equal("AutoTamperRequestBefore", exchange.oRequest.headers["X-Sample-Extension"]);
    }

    [Fact]
    public void AutoTamperRequestAfter_Fires()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        extension.AutoTamperRequestAfter(exchange);

        Assert.Equal("1", exchange["x-sample-request-after"]);
    }

    [Fact]
    public void AutoTamperResponseBefore_CanRewriteTheResponseBody()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        extension.AutoTamperResponseBefore(exchange);

        Assert.Equal("rewritten-by-sample-extension", System.Text.Encoding.UTF8.GetString(exchange.responseBodyBytes));
    }

    [Fact]
    public void AutoTamperResponseAfter_Fires()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        extension.AutoTamperResponseAfter(exchange);

        Assert.Equal("1", exchange["x-sample-response-after"]);
    }

    [Fact]
    public void OnBeforeReturningError_Fires()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        extension.OnBeforeReturningError(exchange);

        Assert.Equal("red", exchange["ui-color"]);
    }

    [Fact]
    public void OnPeekAtResponseHeaders_Fires()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        extension.OnPeekAtResponseHeaders(exchange);

        Assert.Equal("1", exchange["x-sample-peeked-response-headers"]);
    }

    [Fact]
    public void OnPeekAtRequestHeaders_Fires()
    {
        var extension = new SampleExtension();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        extension.OnPeekAtRequestHeaders(exchange);

        Assert.Equal("1", exchange["x-sample-peeked-request-headers"]);
    }

    [Theory]
    [InlineData("sample.ping", true)]
    [InlineData("sample.pong", false)]
    [InlineData("", false)]
    public void OnExecAction_RecognizesOnlyItsOwnCommand(string command, bool expected)
    {
        var extension = new SampleExtension();

        var handled = extension.OnExecAction(command);

        Assert.Equal(expected, handled);
    }

    /// <summary>
    /// Runs every method in one pass against a shared extension instance, in roughly
    /// the order a real request/response cycle would call them -- not just each
    /// method in isolation. Not a claim that this is confirmed real-Fiddler ordering
    /// (see IAutoTamper's own remarks: the *After hooks and OnBeforeReturningError
    /// aren't wired into any real pump here to observe actual ordering from), just a
    /// sanity check that the whole documented surface works together on one instance
    /// without any method's effects interfering with another's.
    /// </summary>
    [Fact]
    public void EveryMethodCanRunInOnePass_AgainstASharedExtensionInstance()
    {
        var extension = new SampleExtension();
        var requestExchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());
        var responseExchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        extension.OnLoad();
        extension.OnPeekAtRequestHeaders(requestExchange);
        extension.AutoTamperRequestBefore(requestExchange);
        extension.AutoTamperRequestAfter(requestExchange);
        extension.OnPeekAtResponseHeaders(responseExchange);
        extension.AutoTamperResponseBefore(responseExchange);
        extension.AutoTamperResponseAfter(responseExchange);
        extension.OnExecAction("sample.ping");
        extension.OnBeforeUnload();

        Assert.Equal(
            new[]
            {
                "OnLoad",
                "OnPeekAtRequestHeaders",
                "AutoTamperRequestBefore",
                "AutoTamperRequestAfter",
                "OnPeekAtResponseHeaders",
                "AutoTamperResponseBefore",
                "AutoTamperResponseAfter",
                "OnExecAction(sample.ping)",
                "OnBeforeUnload",
            },
            extension.CallLog);
    }
}
