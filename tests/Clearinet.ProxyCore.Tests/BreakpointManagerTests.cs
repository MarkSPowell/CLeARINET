using Clearinet.ProxyCore.Breakpoints;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class BreakpointManagerTests
{
    [Fact]
    public async Task ApplyRequestBreakpointAsync_WithNoRulesActive_ReturnsTheRequestUnchangedAndNeverFiresAnEvent()
    {
        var manager = new BreakpointManager();
        var hit = false;
        manager.BreakpointHit += _ => hit = true;
        var request = SampleRequest();

        var result = await manager.ApplyRequestBreakpointAsync("example.test", request, CancellationToken.None);

        Assert.Same(request, result);
        Assert.False(hit);
    }

    [Fact]
    public async Task ApplyRequestBreakpointAsync_WhenBreakOnAllRequestsIsSet_PausesUntilResumed()
    {
        var manager = new BreakpointManager();
        manager.Rules.BreakOnAllRequests = true;
        PendingBreakpoint? seen = null;
        var resolved = false;
        manager.BreakpointHit += pending => seen = pending;
        manager.BreakpointResolved += _ => resolved = true;

        var task = manager.ApplyRequestBreakpointAsync("example.test", SampleRequest(), CancellationToken.None);

        // Give the manager's continuation a moment to invoke BreakpointHit
        // before asserting on it -- ApplyRequestBreakpointAsync is a single
        // async method, so by the time control returns to this test thread
        // past the await, the event has already fired synchronously inline.
        Assert.NotNull(seen);
        Assert.False(task.IsCompleted);

        seen!.Resume();
        var result = await task;

        Assert.Same(seen.Request, result);
        Assert.True(resolved);
    }

    [Fact]
    public async Task ApplyRequestBreakpointAsync_ResumesWithTheEditedRequestWhenOneWasApplied()
    {
        var manager = new BreakpointManager();
        manager.Rules.BreakOnAllRequests = true;
        PendingBreakpoint? seen = null;
        manager.BreakpointHit += pending => seen = pending;

        var task = manager.ApplyRequestBreakpointAsync("example.test", SampleRequest(), CancellationToken.None);

        var editedOk = seen!.TryEdit("PUT /edited HTTP/1.1\r\nHost: example.test\r\n\r\nnew body");
        Assert.True(editedOk);
        seen.Resume();

        var result = await task;

        Assert.Equal("PUT", result.Method);
        Assert.Equal("/edited", result.Target);
    }

    [Fact]
    public async Task ApplyRequestBreakpointAsync_ThrowsWhenAborted()
    {
        var manager = new BreakpointManager();
        manager.Rules.BreakOnAllRequests = true;
        PendingBreakpoint? seen = null;
        manager.BreakpointHit += pending => seen = pending;

        var task = manager.ApplyRequestBreakpointAsync("example.test", SampleRequest(), CancellationToken.None);
        seen!.Abort();

        await Assert.ThrowsAsync<BreakpointAbortedException>(() => task);
    }

    [Fact]
    public async Task ApplyResponseBreakpointAsync_MatchesOnResponseStatusCode()
    {
        var manager = new BreakpointManager();
        manager.Rules.ResponseStatusCodeEquals = 404;
        PendingBreakpoint? seen = null;
        manager.BreakpointHit += pending => seen = pending;

        var task = manager.ApplyResponseBreakpointAsync(
            "example.test", SampleRequest(), SampleResponse(404), CancellationToken.None);

        Assert.NotNull(seen);
        seen!.Resume();
        var result = await task;

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ApplyResponseBreakpointAsync_WithANonMatchingStatusCode_NeverPauses()
    {
        var manager = new BreakpointManager();
        manager.Rules.ResponseStatusCodeEquals = 404;
        var hit = false;
        manager.BreakpointHit += _ => hit = true;

        var result = await manager.ApplyResponseBreakpointAsync(
            "example.test", SampleRequest(), SampleResponse(200), CancellationToken.None);

        Assert.False(hit);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public void WouldBreakBeforeRequest_MatchesApplyRequestBreakpointAsyncsOwnDecisionWithoutPausingAnything()
    {
        var manager = new BreakpointManager();
        manager.Rules.RequestMethodEquals = "POST";
        var hit = false;
        manager.BreakpointHit += _ => hit = true;

        Assert.True(manager.WouldBreakBeforeRequest(SampleRequest() with { Method = "POST" }));
        Assert.False(manager.WouldBreakBeforeRequest(SampleRequest() with { Method = "GET" }));
        Assert.False(hit);
    }

    [Fact]
    public void WouldBreakBeforeResponse_MatchesApplyResponseBreakpointAsyncsOwnDecisionWithoutPausingAnything()
    {
        var manager = new BreakpointManager();
        manager.Rules.ResponseStatusCodeEquals = 404;
        var hit = false;
        manager.BreakpointHit += _ => hit = true;

        Assert.True(manager.WouldBreakBeforeResponse(SampleRequest(), SampleResponse(404)));
        Assert.False(manager.WouldBreakBeforeResponse(SampleRequest(), SampleResponse(200)));
        Assert.False(hit);
    }

    [Fact]
    public void PendingBreakpoint_TryEdit_ReportsAnErrorForMalformedTextAndKeepsResumeBlocked()
    {
        var manager = new BreakpointManager();
        manager.Rules.BreakOnAllRequests = true;
        PendingBreakpoint? seen = null;
        manager.BreakpointHit += pending => seen = pending;

        _ = manager.ApplyRequestBreakpointAsync("example.test", SampleRequest(), CancellationToken.None);

        // No spaces at all, so it can't even shape-check as "METHOD /target
        // HTTP/1.1" -- see HttpMessageTextTests for why a line merely
        // containing nonsense words still parses (matches
        // Http1MessageReader's own leniency about token content).
        var ok = seen!.TryEdit("malformed");

        Assert.False(ok);
        Assert.False(seen.IsValid);
        Assert.Throws<InvalidOperationException>(() => seen.Resume());
    }

    private static CapturedRequest SampleRequest() =>
        new("GET", "/", "HTTP/1.1", [("Host", "example.test")], []);

    private static CapturedResponse SampleResponse(int statusCode) =>
        new("HTTP/1.1", statusCode, "Status", [], []);
}
