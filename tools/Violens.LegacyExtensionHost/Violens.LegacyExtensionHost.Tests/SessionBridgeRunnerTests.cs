using System;
using System.Collections.Generic;
using System.Linq;
using Violens.CompatShim;
using Violens.LegacyExtensionHost.Bridge;
using Xunit;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// <see cref="SessionBridgeRunner"/> and <see cref="SessionMapping"/> without
/// a pipe: wire messages become correct sessions, extension edits come
/// back out, and a throwing extension doesn't stop the others.
/// <see cref="SessionBridgeServerTests"/> covers the real pipe.
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
        // Each tamper appends a marker, so the second must see the first's edit.
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
        // Session has no HTTP version, so it passes through from the original.
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
        // One extension throwing mustn't stop the others or the call.
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
        // Repeated header names aren't supported (see SessionMapping.SetHeaders).
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

    /// <summary>A configurable fake <see cref="IAutoTamper"/>.</summary>
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
