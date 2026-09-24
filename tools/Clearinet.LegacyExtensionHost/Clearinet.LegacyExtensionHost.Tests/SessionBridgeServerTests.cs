using System;
using System.Collections.Generic;
using System.IO.Pipes;
using Clearinet.LegacyExtensionHost.Bridge;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// The real, end-to-end version of <see cref="SessionBridgeRunnerTests"/>:
/// an actual <see cref="NamedPipeClientStream"/> talking to the actual
/// <see cref="SessionBridgeServer"/> (started once by
/// <see cref="SessionBridgeServerFixture"/>), over the same
/// <see cref="SessionBridgeProtocol"/> framing the main app's own
/// <c>LegacyExtensionHostBridgeClient</c> uses -- proves the wire framing,
/// JSON serialization, and pipe transport actually work together, not just
/// the request/response handling logic <see cref="SessionBridgeRunnerTests"/>
/// already covers directly. Deliberately doesn't reference
/// <c>Clearinet.Compatibility.Extensions.LegacyExtensionHostBridgeClient</c>
/// itself (a net10.0 project this net48 test project can't reference at
/// all) -- <see cref="Exchange"/> below is this test's own small, direct
/// stand-in for that client's connect/write/read sequence.
/// </summary>
[Collection(SessionBridgeServerCollection.Name)]
public sealed class SessionBridgeServerTests
{
    [Fact]
    public void Capabilities_ReportsTheOneLoadedFakeAutoTamper()
    {
        var response = Exchange(new BridgeRequestMessage { Kind = BridgeMessageKind.Capabilities });

        Assert.True(response.Ok);
        Assert.Equal(1, response.AutoTamperCount);
    }

    [Fact]
    public void RequestBefore_ARealRoundTrip_ReachesTheLoadedExtensionAndReturnsItsEdit()
    {
        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestBefore,
            SessionOrdinal = 1,
            Hostname = "example.com",
            Request = new WireRequest
            {
                Method = "GET",
                Target = "/",
                HttpVersion = "HTTP/1.1",
                Headers = new List<WireHeader> { new() { Name = "X-Echo", Value = "hello-over-a-real-pipe" } },
                Body = Array.Empty<byte>(),
            },
        };

        var response = Exchange(message);

        Assert.True(response.Ok);
        Assert.NotNull(response.Request);
        var echoed = response.Request!.Headers.Find(h => string.Equals(h.Name, "X-Echo-Seen", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(echoed);
        Assert.Equal("hello-over-a-real-pipe", echoed.Value);
    }

    [Fact]
    public void ResponseBefore_ARealRoundTrip_ReturnsTheExtensionsRewrittenBody()
    {
        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.ResponseBefore,
            SessionOrdinal = 2,
            Hostname = "example.com",
            Request = new WireRequest { Method = "GET", Target = "/", HttpVersion = "HTTP/1.1", Headers = new List<WireHeader>(), Body = Array.Empty<byte>() },
            Response = new WireResponse
            {
                HttpVersion = "HTTP/1.1",
                StatusCode = 200,
                ReasonPhrase = "OK",
                Headers = new List<WireHeader>(),
                Body = System.Text.Encoding.UTF8.GetBytes("original body"),
            },
        };

        var response = Exchange(message);

        Assert.True(response.Ok);
        Assert.NotNull(response.Response);
        Assert.Equal("rewritten-by-EchoAutoTamper", System.Text.Encoding.UTF8.GetString(response.Response!.Body));
    }

    [Fact]
    public void SeveralCallsInARow_AllSucceed()
    {
        // Not a concurrency stress test -- just confirms the "one
        // connection per call, server instance goes back to
        // WaitForConnection afterward" cycle (see SessionBridgeServer's own
        // remarks) actually cycles correctly rather than only working once.
        for (var i = 0; i < 5; i++)
        {
            var response = Exchange(new BridgeRequestMessage { Kind = BridgeMessageKind.Capabilities });
            Assert.True(response.Ok);
        }
    }

    private static BridgeResponseMessage Exchange(BridgeRequestMessage message)
    {
        using var pipe = new NamedPipeClientStream(".", SessionBridgeProtocol.PipeName, PipeDirection.InOut, PipeOptions.None);
        pipe.Connect(5000); // generous vs. the production client's own short timeout -- this test isn't checking connect speed, just correctness.
        SessionBridgeProtocol.WriteMessage(pipe, message);
        return SessionBridgeProtocol.ReadMessage<BridgeResponseMessage>(pipe);
    }
}
