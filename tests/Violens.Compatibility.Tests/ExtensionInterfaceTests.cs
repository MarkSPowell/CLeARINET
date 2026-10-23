using Violens.Compatibility.Extensions;
using Violens.Compatibility.FiddlerScript;
using Violens.ProxyCore.Http;
using Xunit;

namespace Violens.Compatibility.Tests;

/// <summary>
/// Calls every documented member of IFiddlerExtension, IAutoTamper(2/3) and
/// IHandleExecAction on <see cref="SampleExtension"/>, and checks each one's
/// effect on the <see cref="Exchange"/>, not just that it was called.
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
    /// Every method in one pass on one instance, roughly in request/response
    /// order, to check they don't interfere. Not a claim about Fiddler's exact
    /// ordering.
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
