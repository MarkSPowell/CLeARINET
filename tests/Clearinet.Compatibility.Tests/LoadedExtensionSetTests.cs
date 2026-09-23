using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.Compatibility.Tests;

/// <summary>
/// Exercises <see cref="LoadedExtensionSet"/> through its
/// <see cref="IExtensionAutoTamperHost"/> surface specifically -- the same
/// narrow contract <c>InterceptingProxyListener</c> actually calls -- the
/// same reasoning <see cref="FiddlerScriptRunnerTests"/>' own remarks give
/// for testing <c>FiddlerScriptRunner</c> through <c>IFiddlerScriptRunner</c>
/// rather than <c>FiddlerScriptHost</c> directly.
///
/// Every <see cref="IAutoTamper"/> here is a small, purpose-built fake
/// (<see cref="ScriptedAutoTamper"/>) rather than a real compiled extension
/// .dll -- exactly the point of splitting <see cref="ExtensionHost"/>'s
/// disk/reflection/<c>AssemblyLoadContext</c> scanning out from this pure
/// class, per its own remarks: no real extension assembly is needed
/// anywhere in this test suite.
/// </summary>
public class LoadedExtensionSetTests
{
    private static CapturedRequest SampleRequest(string method = "GET", string target = "/widgets") =>
        new(method, target, "HTTP/1.1", [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse() =>
        new("HTTP/1.1", 200, "OK", [], "original"u8.ToArray());

    [Fact]
    public void NoTampersLoaded_HasFlagsAreFalseAndBeforeHooksAreANoOp()
    {
        IExtensionAutoTamperHost host = new LoadedExtensionSet([]);

        Assert.False(host.HasAnyRequestBeforeHandlers);
        Assert.False(host.HasAnyResponseBeforeHandlers);

        var request = SampleRequest();
        // Same instance back, not just an equal one -- confirms no Exchange
        // is even built for the common "nothing loaded" case, the same
        // no-op-when-empty contract IFiddlerScriptRunner's own no-op test
        // checks for.
        Assert.Same(request, host.RunRequestBefore(1, "api.example.com", request));

        var response = SampleResponse();
        Assert.Same(response, host.RunResponseBefore(1, "api.example.com", request, response));

        // Fire-and-observe hooks: just confirming these don't throw with
        // nothing loaded.
        host.RunRequestAfter(1, "api.example.com", request);
        host.RunResponseAfter(1, "api.example.com", request, response);
    }

    [Fact]
    public void AnyTamperLoaded_HasFlagsAreTrue_EvenOneThatOnlyDefinesOtherHooks()
    {
        // Unlike IFiddlerScriptRunner's per-handler HasOnBeforeRequest/
        // HasOnBeforeResponse (a FiddlerScript file can define just one of
        // the two), a C# class implementing IAutoTamper must implement
        // every method on the interface -- so LoadedExtensionSet's flags
        // are gated on "is anything loaded at all," not on which hook a
        // given tamper actually does something in.
        IExtensionAutoTamperHost host = new LoadedExtensionSet([new ScriptedAutoTamper()]);

        Assert.True(host.HasAnyRequestBeforeHandlers);
        Assert.True(host.HasAnyResponseBeforeHandlers);
    }

    [Fact]
    public void RunRequestBefore_RunsLoadedTampersInOrder_EachSeeingThePreviousOnesEdits()
    {
        var first = new ScriptedAutoTamper(onRequestBefore: ex => ex.oRequest.headers["X-Trace"] = "a");
        var second = new ScriptedAutoTamper(onRequestBefore: ex =>
            ex.oRequest.headers["X-Trace"] = ex.oRequest.headers["X-Trace"] + "b");
        IExtensionAutoTamperHost host = new LoadedExtensionSet([first, second]);

        var result = host.RunRequestBefore(1, "api.example.com", SampleRequest());

        var traceHeader = result.Headers.Single(h => h.Name == "X-Trace");
        Assert.Equal("ab", traceHeader.Value);
    }

    [Fact]
    public void RunRequestBefore_OneTamperThrowing_DoesNotStopTheOthersOrPropagate()
    {
        var throwing = new ScriptedAutoTamper(onRequestBefore: _ => throw new InvalidOperationException("boom"));
        var wellBehaved = new ScriptedAutoTamper(onRequestBefore: ex => ex.oRequest.headers["X-Trace"] = "ran");
        IExtensionAutoTamperHost host = new LoadedExtensionSet([throwing, wellBehaved]);

        var result = host.RunRequestBefore(1, "api.example.com", SampleRequest());

        Assert.Equal("ran", result.Headers.Single(h => h.Name == "X-Trace").Value);
    }

    [Fact]
    public void RunRequestAfter_InvokesEveryLoadedTamper()
    {
        var callCount = 0;
        var first = new ScriptedAutoTamper(onRequestAfter: _ => callCount++);
        var second = new ScriptedAutoTamper(onRequestAfter: _ => callCount++);
        IExtensionAutoTamperHost host = new LoadedExtensionSet([first, second]);

        host.RunRequestAfter(1, "api.example.com", SampleRequest());

        Assert.Equal(2, callCount);
    }

    [Fact]
    public void RunResponseBefore_RunsLoadedTampersInOrder_EachSeeingThePreviousOnesEdits()
    {
        var first = new ScriptedAutoTamper(onResponseBefore: ex => ex.oResponse.headers["X-Trace"] = "a");
        var second = new ScriptedAutoTamper(onResponseBefore: ex =>
            ex.oResponse.headers["X-Trace"] = ex.oResponse.headers["X-Trace"] + "b");
        IExtensionAutoTamperHost host = new LoadedExtensionSet([first, second]);

        var result = host.RunResponseBefore(1, "api.example.com", SampleRequest(), SampleResponse());

        Assert.Equal("ab", result.Headers.Single(h => h.Name == "X-Trace").Value);
    }

    [Fact]
    public void RunResponseAfter_InvokesEveryLoadedTamper()
    {
        var callCount = 0;
        var tamper = new ScriptedAutoTamper(onResponseAfter: _ => callCount++);
        IExtensionAutoTamperHost host = new LoadedExtensionSet([tamper]);

        host.RunResponseAfter(1, "api.example.com", SampleRequest(), SampleResponse());

        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// A minimal, original <see cref="IAutoTamper"/> fake for this test
    /// suite -- each hook is an optional delegate so a test wires up only
    /// the one it cares about, the rest defaulting to a no-op.
    /// </summary>
    private sealed class ScriptedAutoTamper(
        Action<Exchange>? onRequestBefore = null,
        Action<Exchange>? onRequestAfter = null,
        Action<Exchange>? onResponseBefore = null,
        Action<Exchange>? onResponseAfter = null) : IAutoTamper
    {
        public void OnLoad()
        {
        }

        public void OnBeforeUnload()
        {
        }

        public void AutoTamperRequestBefore(Exchange oSession) => onRequestBefore?.Invoke(oSession);

        public void AutoTamperRequestAfter(Exchange oSession) => onRequestAfter?.Invoke(oSession);

        public void AutoTamperResponseBefore(Exchange oSession) => onResponseBefore?.Invoke(oSession);

        public void AutoTamperResponseAfter(Exchange oSession) => onResponseAfter?.Invoke(oSession);

        public void OnBeforeReturningError(Exchange oSession)
        {
        }
    }
}
