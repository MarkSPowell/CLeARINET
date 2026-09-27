using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Http;
using Clearinet.SampleExtension;
using Xunit;

namespace Clearinet.SampleExtension.Tests;

/// <summary>
/// Calls every method <see cref="SampleAutoTamper"/> implements directly,
/// including the three <c>InterceptingProxyListener</c> doesn't call yet
/// (<c>OnBeforeReturningError</c>, <c>OnPeekAtResponseHeaders</c>,
/// <c>OnPeekAtRequestHeaders</c>) -- this is the fast, no-desktop-app,
/// no-dropped-.dll check that the interface contract itself is fully
/// implemented and does what its own doc comments say;
/// <c>tools/Clearinet.SampleExtension/README.md</c> covers the slower,
/// manual checks that prove the wiring around it.
/// </summary>
public class SampleAutoTamperTests
{
    private static CapturedRequest SampleRequest() =>
        new("GET", "/widgets", "HTTP/1.1", [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse() =>
        new("HTTP/1.1", 200, "OK", [], []);

    [Fact]
    public void OnLoad_SetsLoaded()
    {
        var tamper = new SampleAutoTamper();
        Assert.False(tamper.Loaded);

        tamper.OnLoad();

        Assert.True(tamper.Loaded);
    }

    [Fact]
    public void OnBeforeUnload_SetsUnloaded()
    {
        var tamper = new SampleAutoTamper();
        Assert.False(tamper.Unloaded);

        tamper.OnBeforeUnload();

        Assert.True(tamper.Unloaded);
    }

    [Fact]
    public void AutoTamperRequestBefore_SetsMarkerHeaderOnTheRequest()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        tamper.AutoTamperRequestBefore(exchange);

        Assert.Equal("AutoTamperRequestBefore-ran", exchange.oRequest.headers[SampleAutoTamper.MarkerHeaderName]);
    }

    [Fact]
    public void AutoTamperRequestAfter_IncrementsCountAndDoesNotTouchHeaders()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        tamper.AutoTamperRequestAfter(exchange);

        Assert.Equal(1, tamper.RequestAfterCount);
        Assert.Null(exchange.oRequest.headers[SampleAutoTamper.MarkerHeaderName]);
    }

    [Fact]
    public void AutoTamperResponseBefore_SetsMarkerHeaderOnTheResponse()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        tamper.AutoTamperResponseBefore(exchange);

        Assert.Equal("AutoTamperResponseBefore-ran", exchange.oResponse.headers[SampleAutoTamper.MarkerHeaderName]);
    }

    [Fact]
    public void AutoTamperResponseAfter_IncrementsCount()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        tamper.AutoTamperResponseAfter(exchange);

        Assert.Equal(1, tamper.ResponseAfterCount);
    }

    [Fact]
    public void OnBeforeReturningError_IncrementsCount()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        tamper.OnBeforeReturningError(exchange);

        Assert.Equal(1, tamper.BeforeReturningErrorCount);
    }

    [Fact]
    public void OnPeekAtResponseHeaders_IncrementsCount()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForResponse(1, "api.example.com", SampleRequest(), SampleResponse());

        tamper.OnPeekAtResponseHeaders(exchange);

        Assert.Equal(1, tamper.PeekResponseHeadersCount);
    }

    [Fact]
    public void OnPeekAtRequestHeaders_IncrementsCount()
    {
        var tamper = new SampleAutoTamper();
        var exchange = Exchange.ForRequest(1, "api.example.com", SampleRequest());

        tamper.OnPeekAtRequestHeaders(exchange);

        Assert.Equal(1, tamper.PeekRequestHeadersCount);
    }
}
