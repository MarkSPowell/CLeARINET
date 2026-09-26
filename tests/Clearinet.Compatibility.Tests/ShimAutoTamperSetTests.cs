using System.Text;
using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Http;
using Xunit;
using IAutoTamper3 = Clearinet.CompatShim.IAutoTamper3;
using Session = Clearinet.CompatShim.Session;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// <see cref="ShimAutoTamperSet"/>: ported Fiddler-shaped IAutoTamper hooks
/// run against one <see cref="Session"/> per request.
/// </summary>
public class ShimAutoTamperSetTests
{
    private static CapturedRequest Request(string target = "/", string body = "") =>
        new("POST", target, "HTTP/1.1", [("Host", "example.test"), ("Content-Type", "text/plain")], Encoding.UTF8.GetBytes(body));

    private static CapturedResponse Response(string body = "hello") =>
        new("HTTP/1.1", 200, "OK", [("Content-Type", "text/plain")], Encoding.UTF8.GetBytes(body));

    [Fact]
    public void OneSessionObjectLastsFromTheFirstHookToTheLast()
    {
        var recorder = new Recorder();
        var scope = new ShimAutoTamperSet([recorder]).BeginSession(1, "example.test", "https");

        var request = Request();
        scope.PeekAtRequestHeaders(request with { Body = [] });
        var result = scope.RequestBefore(request);
        scope.RequestAfter(result.Request);
        var response = Response();
        scope.PeekAtResponseHeaders(result.Request, response with { Body = [] });
        var edited = scope.ResponseBefore(result.Request, response);
        scope.ResponseAfter(result.Request, edited);

        Assert.Equal(
            new[] { "OnPeekAtRequestHeaders", "AutoTamperRequestBefore", "AutoTamperRequestAfter", "OnPeekAtResponseHeaders", "AutoTamperResponseBefore", "AutoTamperResponseAfter" },
            recorder.Calls.Select(c => c.Hook).ToArray());
        Assert.Single(recorder.Calls.Select(c => c.Session).Distinct());
    }

    [Fact]
    public void FlagsSetEarlySurviveToTheRecordedSession()
    {
        var scope = new ShimAutoTamperSet([new Flagger()]).BeginSession(1, "example.test", "https");

        var result = scope.RequestBefore(Request());
        scope.ResponseBefore(result.Request, Response());

        Assert.NotNull(scope.Flags);
        Assert.Equal("seen in request", scope.Flags!["x-note"]);
        Assert.Equal("seen in request", scope.Flags["x-response-saw"]);
    }

    [Fact]
    public void EditsAreSentAndSessionDetailsLookLikeFiddlers()
    {
        var scope = new ShimAutoTamperSet([new Editor()]).BeginSession(1, "example.test", "https");

        var result = scope.RequestBefore(Request("/path?q=1", "abc"));
        var response = scope.ResponseBefore(result.Request, Response());

        Assert.Null(result.LocalResponse);
        Assert.Contains(result.Request.Headers, h => h.Name == "X-Url" && h.Value == "https://example.test/path?q=1");
        Assert.Equal("ABC", Encoding.UTF8.GetString(result.Request.Body));
        Assert.Equal(2, response.Headers.Count(h => h.Name == "Set-Cookie"));
        Assert.Equal("hello!", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void BypassingTheServerReturnsTheExtensionsResponse()
    {
        var scope = new ShimAutoTamperSet([new Answerer()]).BeginSession(1, "example.test", "https");

        var result = scope.RequestBefore(Request());

        Assert.NotNull(result.LocalResponse);
        Assert.Equal(200, result.LocalResponse!.StatusCode);
        Assert.Equal("OK", result.LocalResponse.ReasonPhrase);
        Assert.Equal("answered", Encoding.UTF8.GetString(result.LocalResponse.Body));
        Assert.Contains(result.LocalResponse.Headers, h => h.Name == "Content-Type" && h.Value == "text/plain");
    }

    [Fact]
    public void ChangesMadeBetweenHooksArePickedUpWithoutLosingFlags()
    {
        var recorder = new Recorder();
        var scope = new ShimAutoTamperSet([new Flagger(), recorder]).BeginSession(1, "example.test", "https");

        var result = scope.RequestBefore(Request());
        // FiddlerScript or a breakpoint replaced the request after the hooks ran.
        var replaced = result.Request with { Target = "/changed" };
        scope.RequestAfter(replaced);

        Assert.Equal("/changed", recorder.LastPathAndQuery);
        Assert.Equal("seen in request", scope.Flags!["x-note"]);
    }

    [Fact]
    public void AThrowingExtensionDoesNotStopTheOthers()
    {
        var log = new List<string>();
        var recorder = new Recorder();
        var scope = new ShimAutoTamperSet([new Thrower(), recorder], log.Add).BeginSession(1, "example.test", "https");

        scope.RequestBefore(Request());

        Assert.Contains("AutoTamperRequestBefore", recorder.Calls.Select(c => c.Hook));
        Assert.Contains(log, line => line.Contains(nameof(Thrower)) && line.Contains("boom"));
    }

    [Fact]
    public void HeaderChangesMadeWhilePeekingAreSent()
    {
        var scope = new ShimAutoTamperSet([new HeaderRenamer()]).BeginSession(1, "example.test", "https");
        var request = Request();
        var response = Response() with { Headers = [("P3P", "bad"), ("Content-Type", "text/plain")] };

        var result = scope.RequestBefore(request);
        scope.PeekAtResponseHeaders(result.Request, response with { Body = [] });
        var sent = scope.ResponseBefore(result.Request, response);

        Assert.DoesNotContain(sent.Headers, h => h.Name == "P3P");
        Assert.Contains(sent.Headers, h => h.Name == "Renamed-P3P" && h.Value == "bad");
    }

    [Fact]
    public void PeekChangesYieldToChangesMadeInBetween()
    {
        var scope = new ShimAutoTamperSet([new HeaderRenamer()]).BeginSession(1, "example.test", "https");
        var result = scope.RequestBefore(Request());
        var response = Response() with { Headers = [("P3P", "bad")] };

        scope.PeekAtResponseHeaders(result.Request, response);
        // FiddlerScript or a breakpoint changed the headers before the body arrived.
        var sent = scope.ResponseBefore(result.Request, response with { Headers = [("P3P", "fixed")] });

        Assert.Equal(("P3P", "fixed"), Assert.Single(sent.Headers));
    }

    [Fact]
    public void WithNoExtensionsItIsInactive()
    {
        Assert.False(new ShimAutoTamperSet([]).IsActive);
        Assert.True(new ShimAutoTamperSet([new Recorder()]).IsActive);
    }

    private abstract class TamperBase : IAutoTamper3
    {
        public virtual void OnLoad() { }
        public virtual void OnBeforeUnload() { }
        public virtual void AutoTamperRequestBefore(Session oSession) { }
        public virtual void AutoTamperRequestAfter(Session oSession) { }
        public virtual void AutoTamperResponseBefore(Session oSession) { }
        public virtual void AutoTamperResponseAfter(Session oSession) { }
        public virtual void OnBeforeReturningError(Session oSession) { }
        public virtual void OnPeekAtResponseHeaders(Session oSession) { }
        public virtual void OnPeekAtRequestHeaders(Session oSession) { }
    }

    private sealed class Recorder : TamperBase
    {
        public List<(string Hook, Session Session)> Calls { get; } = [];

        public string? LastPathAndQuery { get; private set; }

        public override void OnPeekAtRequestHeaders(Session oSession) => Record(nameof(OnPeekAtRequestHeaders), oSession);
        public override void AutoTamperRequestBefore(Session oSession) => Record(nameof(AutoTamperRequestBefore), oSession);
        public override void AutoTamperRequestAfter(Session oSession) => Record(nameof(AutoTamperRequestAfter), oSession);
        public override void OnPeekAtResponseHeaders(Session oSession) => Record(nameof(OnPeekAtResponseHeaders), oSession);
        public override void AutoTamperResponseBefore(Session oSession) => Record(nameof(AutoTamperResponseBefore), oSession);
        public override void AutoTamperResponseAfter(Session oSession) => Record(nameof(AutoTamperResponseAfter), oSession);

        private void Record(string hook, Session session)
        {
            Calls.Add((hook, session));
            LastPathAndQuery = session.PathAndQuery;
        }
    }

    private sealed class Flagger : TamperBase
    {
        public override void AutoTamperRequestBefore(Session oSession) => oSession["x-note"] = "seen in request";

        public override void AutoTamperResponseBefore(Session oSession) => oSession["x-response-saw"] = oSession["x-note"];
    }

    private sealed class Editor : TamperBase
    {
        public override void AutoTamperRequestBefore(Session oSession)
        {
            oSession.oRequest.headers.Add("X-Url", oSession.fullUrl);
            oSession.RequestBody = Encoding.UTF8.GetBytes(oSession.GetRequestBodyAsString().ToUpperInvariant());
        }

        public override void AutoTamperResponseBefore(Session oSession)
        {
            oSession.oResponse.headers.Add("Set-Cookie", "a=1");
            oSession.oResponse.headers.Add("Set-Cookie", "b=2");
            oSession.ResponseBody = Encoding.UTF8.GetBytes(oSession.GetResponseBodyAsString() + "!");
        }
    }

    private sealed class Answerer : TamperBase
    {
        public override void AutoTamperRequestBefore(Session oSession)
        {
            oSession.utilCreateResponseAndBypassServer();
            oSession.oResponse.headers.Add("Content-Type", "text/plain");
            oSession.ResponseBody = Encoding.UTF8.GetBytes("answered");
        }
    }

    private sealed class HeaderRenamer : TamperBase
    {
        public override void OnPeekAtResponseHeaders(Session oSession)
        {
            var value = oSession.oResponse.headers["P3P"];
            if (value is not null)
            {
                oSession.oResponse.headers["Renamed-P3P"] = value;
                oSession.oResponse.headers.Remove("P3P");
            }
        }
    }

        private sealed class Thrower : TamperBase
    {
        public override void AutoTamperRequestBefore(Session oSession) => throw new InvalidOperationException("boom");
    }
}
