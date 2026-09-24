using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// Exercises <see cref="CompositeExtensionAutoTamperHost"/> against small,
/// purpose-built fake <see cref="IExtensionAutoTamperHost"/> implementations
/// -- no real <c>LoadedExtensionSet</c> or
/// <c>LegacyExtensionHostBridgeClient</c> needed here, matching the same
/// "test through the interface, with fakes" approach
/// <c>Clearinet.Compatibility.Tests.LoadedExtensionSetTests</c> already
/// uses for the layer below this one.
/// </summary>
public sealed class CompositeExtensionAutoTamperHostTests
{
    private static CapturedRequest SampleRequest() => new("GET", "/widgets", "HTTP/1.1", [("Host", "api.example.com")], []);

    private static CapturedResponse SampleResponse() => new("HTTP/1.1", 200, "OK", [], "original"u8.ToArray());

    [Fact]
    public void NoHosts_HasFlagsAreFalseAndHooksPassThroughUnchanged()
    {
        IExtensionAutoTamperHost composite = new CompositeExtensionAutoTamperHost([]);

        Assert.False(composite.HasAnyRequestBeforeHandlers);
        Assert.False(composite.HasAnyResponseBeforeHandlers);

        var request = SampleRequest();
        Assert.Same(request, composite.RunRequestBefore(1, "api.example.com", request));

        var response = SampleResponse();
        Assert.Same(response, composite.RunResponseBefore(1, "api.example.com", request, response));
    }

    [Fact]
    public void AnyWrappedHostHandlingRequestBefore_MakesHasAnyRequestBeforeHandlersTrue()
    {
        IExtensionAutoTamperHost composite = new CompositeExtensionAutoTamperHost(
            [new FakeHost(hasRequestBefore: false), new FakeHost(hasRequestBefore: true)]);

        Assert.True(composite.HasAnyRequestBeforeHandlers);
    }

    [Fact]
    public void RunRequestBefore_ThreadsTheEditedRequestThroughEveryHostInOrder()
    {
        // Each fake host appends its own marker to the same header -- proves
        // the second host sees the first one's edit, not the original
        // request, mirroring LoadedExtensionSet's own "one shared,
        // progressively edited instance" contract one level up (several
        // extension HOSTS here, rather than several extensions within one
        // host).
        var first = new FakeHost(hasRequestBefore: true, editRequest: r => AppendHeader(r, "X-Trace", "A"));
        var second = new FakeHost(hasRequestBefore: true, editRequest: r => AppendHeader(r, "X-Trace", "B"));
        IExtensionAutoTamperHost composite = new CompositeExtensionAutoTamperHost([first, second]);

        var result = composite.RunRequestBefore(1, "api.example.com", SampleRequest());

        var trace = result.Headers.Single(h => h.Name == "X-Trace").Value;
        Assert.Equal("AB", trace);
    }

    [Fact]
    public void RunResponseBefore_ThreadsTheEditedResponseThroughEveryHostInOrder()
    {
        var first = new FakeHost(hasResponseBefore: true, editResponse: r => r with { Body = r.Body.Concat("-first"u8.ToArray()).ToArray() });
        var second = new FakeHost(hasResponseBefore: true, editResponse: r => r with { Body = r.Body.Concat("-second"u8.ToArray()).ToArray() });
        IExtensionAutoTamperHost composite = new CompositeExtensionAutoTamperHost([first, second]);

        var result = composite.RunResponseBefore(1, "api.example.com", SampleRequest(), SampleResponse());

        Assert.Equal("original-first-second", System.Text.Encoding.UTF8.GetString(result.Body));
    }

    [Fact]
    public void RunRequestAfterAndRunResponseAfter_CallEveryWrappedHost()
    {
        var first = new FakeHost();
        var second = new FakeHost();
        IExtensionAutoTamperHost composite = new CompositeExtensionAutoTamperHost([first, second]);

        composite.RunRequestAfter(1, "api.example.com", SampleRequest());
        composite.RunResponseAfter(1, "api.example.com", SampleRequest(), SampleResponse());

        Assert.True(first.RequestAfterCalled);
        Assert.True(first.ResponseAfterCalled);
        Assert.True(second.RequestAfterCalled);
        Assert.True(second.ResponseAfterCalled);
    }

    private static CapturedRequest AppendHeader(CapturedRequest request, string name, string value)
    {
        var existing = request.Headers.FirstOrDefault(h => h.Name == name);
        var newValue = existing.Name is null ? value : existing.Value + value;
        var headers = request.Headers.Where(h => h.Name != name).Append((name, newValue)).ToList();
        return request with { Headers = headers };
    }

    private sealed class FakeHost : IExtensionAutoTamperHost
    {
        private readonly bool _hasRequestBefore;
        private readonly bool _hasResponseBefore;
        private readonly Func<CapturedRequest, CapturedRequest>? _editRequest;
        private readonly Func<CapturedResponse, CapturedResponse>? _editResponse;

        public FakeHost(bool hasRequestBefore = false, bool hasResponseBefore = false, Func<CapturedRequest, CapturedRequest>? editRequest = null, Func<CapturedResponse, CapturedResponse>? editResponse = null)
        {
            _hasRequestBefore = hasRequestBefore;
            _hasResponseBefore = hasResponseBefore;
            _editRequest = editRequest;
            _editResponse = editResponse;
        }

        public bool RequestAfterCalled { get; private set; }

        public bool ResponseAfterCalled { get; private set; }

        public bool HasAnyRequestBeforeHandlers => _hasRequestBefore;

        public bool HasAnyResponseBeforeHandlers => _hasResponseBefore;

        public CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request) =>
            _editRequest is null ? request : _editRequest(request);

        public void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request) => RequestAfterCalled = true;

        public CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response) =>
            _editResponse is null ? response : _editResponse(response);

        public void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response) => ResponseAfterCalled = true;
    }
}
