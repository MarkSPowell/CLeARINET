using System;
using System.Collections.Generic;
using System.Linq;
using Clearinet.CompatShim;
using Clearinet.LegacyExtensionHost.Bridge;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Direct, no-pipe unit tests of <see cref="SessionBridgeRunner"/> and
/// <see cref="SessionMapping"/> -- the part of the session bridge (see the
/// design doc's "Session bridge" section) most worth pinning down with a
/// fast, deterministic test: does a wire message actually turn into a
/// correct <see cref="Session"/>, does a loaded extension's edit to it
/// actually make it back out, and does one throwing extension leave the
/// others (and the call itself) unaffected. See
/// <see cref="SessionBridgeServerTests"/> for the complementary real,
/// end-to-end pipe version of this same round trip.
///
/// Both classes below are accessible here only because
/// <c>Clearinet.LegacyExtensionHost/AssemblyInfo.cs</c> grants this test
/// assembly <c>InternalsVisibleTo</c> -- they stay <c>internal</c> in the
/// host project itself, genuine implementation details rather than a
/// public surface.
/// </summary>
public sealed class SessionBridgeRunnerTests
{
    [Fact]
    public void Handle_Capabilities_ReturnsTheLoadedAutoTamperCount()
    {
        var runner = new SessionBridgeRunner(new IAutoTamper[] { new RecordingAutoTamper(), new RecordingAutoTamper() }, _ => { });

        var response = runner.Handle(new BridgeRequestMessage { Kind = BridgeMessageKind.Capabilities });

        Assert.True(response.Ok);
        Assert.Equal(2, response.AutoTamperCount);
    }

    [Fact]
    public void Handle_RequestBefore_RunsEveryTamperAgainstOneSharedSessionInOrder()
    {
        // Each tamper appends its own marker to the same header -- if they
        // were each handed an independent Session instead of one shared,
        // progressively-edited one (matching LoadedExtensionSet's own
        // shape -- see that class's own remarks), the second tamper
        // wouldn't see the first one's edit and this header would only
        // ever contain "A".
        var first = new RecordingAutoTamper(onRequestBefore: s => s.oRequest.headers["X-Trace"] = (s.oRequest.headers["X-Trace"] ?? string.Empty) + "A");
        var second = new RecordingAutoTamper(onRequestBefore: s => s.oRequest.headers["X-Trace"] = (s.oRequest.headers["X-Trace"] ?? string.Empty) + "B");
        var runner = new SessionBridgeRunner(new IAutoTamper[] { first, second }, _ => { });

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestBefore,
            SessionOrdinal = 7,
            Hostname = "example.com",
            Request = new WireRequest
            {
                Method = "GET",
                Target = "/index.html?x=1",
                HttpVersion = "HTTP/1.1",
                Headers = new List<WireHeader> { new() { Name = "Host", Value = "example.com" } },
                Body = Array.Empty<byte>(),
            },
        };

        var response = runner.Handle(message);

        Assert.True(response.Ok);
        Assert.NotNull(response.Request);
        Assert.Equal("AB", response.Request!.Headers.Single(h => h.Name == "X-Trace").Value);
        // Untouched fields pass straight through -- see SessionMapping's own
        // remarks on HttpVersion specifically (Session has no member for
        // it at all).
        Assert.Equal("HTTP/1.1", response.Request.HttpVersion);
        Assert.Equal("GET", response.Request.Method);
        Assert.Equal("/index.html?x=1", response.Request.Target);
    }

    [Fact]
    public void Handle_ResponseBefore_ProjectsAnEditedStatusCodeAndBodyBackOut()
    {
        var tamper = new RecordingAutoTamper(onResponseBefore: s =>
        {
            s.responseCode = 404;
            s.oResponse.headers.HTTPResponseStatus = "404 Not Found";
            s.utilSetResponseBody("gone");
        });
        var runner = new SessionBridgeRunner(new IAutoTamper[] { tamper }, _ => { });

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.ResponseBefore,
            SessionOrdinal = 1,
            Hostname = "example.com",
            Request = new WireRequest { Method = "GET", Target = "/", HttpVersion = "HTTP/1.1", Headers = new List<WireHeader>(), Body = Array.Empty<byte>() },
            Response = new WireResponse
            {
                HttpVersion = "HTTP/1.1",
                StatusCode = 200,
                ReasonPhrase = "OK",
                Headers = new List<WireHeader> { new() { Name = "Content-Type", Value = "text/plain" } },
                Body = System.Text.Encoding.UTF8.GetBytes("hello"),
            },
        };

        var response = runner.Handle(message);

        Assert.True(response.Ok);
        Assert.NotNull(response.Response);
        Assert.Equal(404, response.Response!.StatusCode);
        Assert.Equal("Not Found", response.Response.ReasonPhrase);
        Assert.Equal("gone", System.Text.Encoding.UTF8.GetString(response.Response.Body));
        // HttpVersion again has no Session member to have been edited through.
        Assert.Equal("HTTP/1.1", response.Response.HttpVersion);
    }

    [Fact]
    public void Handle_OneTamperThrowing_StillRunsTheOthersAndReturnsOk()
    {
        // Matches LoadedExtensionSet.RunSafely's own contract -- see that
        // method's remarks: a badly-behaved loaded extension shouldn't take
        // the others, or the call itself, down with it.
        var before = new RecordingAutoTamper(onRequestBefore: s => s.oRequest.headers["X-Before"] = "ran");
        var throwing = new RecordingAutoTamper(onRequestBefore: _ => throw new InvalidOperationException("boom"));
        var after = new RecordingAutoTamper(onRequestBefore: s => s.oRequest.headers["X-After"] = "ran");
        var runner = new SessionBridgeRunner(new IAutoTamper[] { before, throwing, after }, _ => { });

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestBefore,
            Hostname = "example.com",
            Request = new WireRequest { Method = "GET", Target = "/", HttpVersion = "HTTP/1.1", Headers = new List<WireHeader>(), Body = Array.Empty<byte>() },
        };

        var response = runner.Handle(message);

        Assert.True(response.Ok);
        Assert.Equal("ran", response.Request!.Headers.Single(h => h.Name == "X-Before").Value);
        Assert.Equal("ran", response.Request.Headers.Single(h => h.Name == "X-After").Value);
    }

    [Fact]
    public void Handle_RequestBeforeWithNoRequestPayload_ReturnsNotOkRatherThanThrowing()
    {
        var runner = new SessionBridgeRunner(Array.Empty<IAutoTamper>(), _ => { });

        var response = runner.Handle(new BridgeRequestMessage { Kind = BridgeMessageKind.RequestBefore, Request = null });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void Handle_RepeatedHeaderName_KeepsOnlyTheLastValue_AndDoesNotThrow()
    {
        // Documents, rather than hides, the known HTTPHeaders limitation --
        // see SessionMapping.SetHeaders' own remarks.
        var runner = new SessionBridgeRunner(Array.Empty<IAutoTamper>(), _ => { });

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestBefore,
            Hostname = "example.com",
            Request = new WireRequest
            {
                Method = "GET",
                Target = "/",
                HttpVersion = "HTTP/1.1",
                Headers = new List<WireHeader>
                {
                    new() { Name = "X-Multi", Value = "first" },
                    new() { Name = "X-Multi", Value = "second" },
                },
                Body = Array.Empty<byte>(),
            },
        };

        var response = runner.Handle(message);

        Assert.True(response.Ok);
        Assert.Equal("second", response.Request!.Headers.Single(h => h.Name == "X-Multi").Value);
    }

    /// <summary>
    /// A minimal, configurable <see cref="IAutoTamper"/> fake -- same role
    /// as the hand-written fakes <c>LoadedExtensionSetTests</c> uses on the
    /// in-process side, adapted to this shim's own <see cref="Session"/>
    /// shape.
    /// </summary>
    private sealed class RecordingAutoTamper : IAutoTamper
    {
        private readonly Action<Session> _onRequestBefore;
        private readonly Action<Session> _onResponseBefore;

        public RecordingAutoTamper(Action<Session> onRequestBefore = null, Action<Session> onResponseBefore = null)
        {
            _onRequestBefore = onRequestBefore ?? (_ => { });
            _onResponseBefore = onResponseBefore ?? (_ => { });
        }

        public void OnLoad() { }

        public void OnBeforeUnload() { }

        public void AutoTamperRequestBefore(Session oSession) => _onRequestBefore(oSession);

        public void AutoTamperRequestAfter(Session oSession) { }

        public void AutoTamperResponseBefore(Session oSession) => _onResponseBefore(oSession);

        public void AutoTamperResponseAfter(Session oSession) { }

        public void OnBeforeReturningError(Session oSession) { }
    }
}
