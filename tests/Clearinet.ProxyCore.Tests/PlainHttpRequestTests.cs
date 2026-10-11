using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Proxy;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>How plain http:// proxy requests are stored and sent on (<see cref="PlainHttpRequest"/>).</summary>
public class PlainHttpRequestTests
{
    private static CapturedRequest Request(string target, params (string Name, string Value)[] headers) =>
        new("GET", target, "HTTP/1.1", headers, []);

    [Theory]
    [InlineData("http://example.test/a?b=1", "/a?b=1", "example.test", "example.test", 80)]
    [InlineData("http://example.test", "/", "example.test", "example.test", 80)]
    [InlineData("http://example.test?q", "/?q", "example.test", "example.test", 80)]
    [InlineData("http://example.test:8080/x#frag", "/x", "example.test:8080", "example.test", 8080)]
    [InlineData("http://user:pw@example.test/", "/", "example.test", "example.test", 80)]
    [InlineData("http://[::1]:81/", "/", "[::1]:81", "::1", 81)]
    [InlineData("HTTP://Example.test/", "/", "Example.test", "Example.test", 80)]
    public void AnAbsoluteUrlBecomesAPathAndAHostHeader(string target, string path, string hostHeader, string host, int port)
    {
        Assert.True(PlainHttpRequest.TryNormalize(Request(target, ("Accept", "*/*")), out var normalized, out var routeHost, out var routePort));

        Assert.Equal(path, normalized.Target);
        Assert.Equal(("Host", hostHeader), normalized.Headers[0]);
        Assert.Contains(("Accept", "*/*"), normalized.Headers);
        Assert.Equal(host, routeHost);
        Assert.Equal(port, routePort);
    }

    [Fact]
    public void TheUrlReplacesAStaleHostHeaderInPlace()
    {
        Assert.True(PlainHttpRequest.TryNormalize(
            Request("http://new.example.test/", ("Accept", "*/*"), ("Host", "old.example.test"), ("host", "dup")),
            out var normalized, out _, out _));

        Assert.Equal(new[] { ("Accept", "*/*"), ("Host", "new.example.test") }, normalized.Headers.ToArray());
    }

    [Fact]
    public void APathIsRoutedByItsHostHeader()
    {
        Assert.True(PlainHttpRequest.TryNormalize(Request("/a", ("Host", "example.test:8000")), out var normalized, out var host, out var port));

        Assert.Equal("/a", normalized.Target);
        Assert.Equal("example.test", host);
        Assert.Equal(8000, port);
    }

    [Theory]
    [InlineData("/no-host")]
    [InlineData("https://example.test/")]
    [InlineData("ftp://example.test/")]
    [InlineData("http:///nohost")]
    [InlineData("http://example.test:0/")]
    [InlineData("http://example.test:99999/")]
    [InlineData("http://::1/")]
    public void RefusesWhatItCantRoute(string target)
    {
        Assert.False(PlainHttpRequest.TryNormalize(Request(target), out _, out _, out _));
    }

    [Fact]
    public void ProxyOnlyHeadersAreNotSentOn()
    {
        var request = Request("/a", ("Host", "example.test"), ("Proxy-Connection", "keep-alive"), ("Proxy-Authorization", "Basic eA=="), ("Accept", "*/*"));

        var direct = PlainHttpRequest.ForUpstream(request, absoluteForm: false);

        Assert.Equal("/a", direct.Target);
        Assert.Equal(new[] { ("Host", "example.test"), ("Accept", "*/*") }, direct.Headers.ToArray());
    }

    [Fact]
    public void ThroughAnotherProxyTheFullUrlGoesBackInTheRequestLine()
    {
        var request = Request("/a?b=1", ("Host", "example.test:8080"));

        Assert.Equal("http://example.test:8080/a?b=1", PlainHttpRequest.ForUpstream(request, absoluteForm: true).Target);
    }
}
