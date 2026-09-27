using System.Net;
using System.Net.Sockets;
using System.Text;
using Clearinet.ProxyCore.Proxy;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>Forwarding through the network's own proxy (<see cref="UpstreamProxy"/>).</summary>
public class UpstreamProxyTests
{
    [Theory]
    [InlineData("proxy.corp:8080", "proxy.corp", 8080)]
    [InlineData("http://proxy.corp:3128/", "proxy.corp", 3128)]
    [InlineData(" 10.0.0.1:80 ", "10.0.0.1", 80)]
    [InlineData("[::1]:3128", "::1", 3128)]
    public void ParsesHostAndPort(string text, string host, int port)
    {
        var proxy = UpstreamProxy.Parse(text);

        Assert.NotNull(proxy);
        Assert.Equal((host, port), (proxy!.Host, proxy.Port));
    }

    [Theory]
    [InlineData("")]
    [InlineData("proxy.corp")]
    [InlineData("proxy.corp:notaport")]
    [InlineData("proxy.corp:70000")]
    [InlineData("socks://proxy.corp:1080")]
    [InlineData("https://proxy.corp:443")]
    [InlineData("a:b:c")]
    public void RejectsWhatItCantUse(string text)
    {
        Assert.Null(UpstreamProxy.Parse(text));
    }

    [Fact]
    public void WindowsPerProtocolSettingsPreferHttps()
    {
        var proxy = UpstreamProxy.FromWindowsSettings("http=web:80;https=secure:443;ftp=ftp:21", "*.corp;<local>");

        Assert.Equal(("secure", 443), (proxy!.Host, proxy.Port));
        Assert.Equal(new[] { "*.corp", "<local>" }, proxy.BypassList.ToArray());
    }

    [Fact]
    public void WindowsSettingsFallBackToHttpThenGiveUpOnSocksOnly()
    {
        Assert.Equal("web", UpstreamProxy.FromWindowsSettings("http=web:80", null)!.Host);
        Assert.Equal("all", UpstreamProxy.FromWindowsSettings("all:8080", null)!.Host);
        Assert.Null(UpstreamProxy.FromWindowsSettings("socks=s:1080", null));
        Assert.Null(UpstreamProxy.FromWindowsSettings("", null));
    }

    [Theory]
    [InlineData("intranet", true)]
    [InlineData("www.example.com", false)]
    [InlineData("build.corp.example", true)]
    [InlineData("corp.example", false)]
    [InlineData("10.1.2.3", true)]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    public void BypassListFollowsTheWindowsRules(string host, bool bypassed)
    {
        var proxy = new UpstreamProxy("proxy", 8080, ["<local>", "*.corp.example", "10.*"]);

        Assert.Equal(bypassed, proxy.ShouldBypass(host));
    }

    [Fact]
    public void RecognisesItself()
    {
        Assert.True(new UpstreamProxy("127.0.0.1", 8888).IsSelf(8888));
        Assert.False(new UpstreamProxy("127.0.0.1", 3128).IsSelf(8888));
        Assert.False(new UpstreamProxy("proxy.corp", 8888).IsSelf(8888));
    }

    [Fact]
    public async Task OpensATunnelWithConnect()
    {
        await using var fake = FakeProxy.Start("HTTP/1.1 200 Connection established\r\n\r\n");

        using var client = await new UpstreamProxy("127.0.0.1", fake.Port).ConnectTunnelAsync("example.test", 443, CancellationToken.None);

        var request = await fake.Received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.StartsWith("CONNECT example.test:443 HTTP/1.1\r\n", request);
        Assert.Contains("Host: example.test:443\r\n", request);
    }

    [Fact]
    public async Task ExplainsAProxyThatWantsASignIn()
    {
        await using var fake = FakeProxy.Start("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Negotiate\r\n\r\n");

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new UpstreamProxy("127.0.0.1", fake.Port).ConnectTunnelAsync("example.test", 443, CancellationToken.None));

        Assert.Contains("authentication", error.Message);
    }

    [Fact]
    public async Task ReportsARefusal()
    {
        await using var fake = FakeProxy.Start("HTTP/1.1 403 Forbidden\r\n\r\n");

        var error = await Assert.ThrowsAsync<IOException>(() =>
            new UpstreamProxy("127.0.0.1", fake.Port).ConnectTunnelAsync("example.test", 443, CancellationToken.None));

        Assert.Contains("403 Forbidden", error.Message);
    }

    /// <summary>A one-connection proxy: records the request head it receives, sends a canned reply, then closes.</summary>
    internal sealed class FakeProxy : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;

        private FakeProxy(string reply)
        {
            _listener.Start();
            _serve = ServeAsync(reply);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public TaskCompletionSource<string> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static FakeProxy Start(string reply) => new(reply);

        private async Task ServeAsync(string reply)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var head = await ReadHeadAsync(stream);
                Received.TrySetResult(head);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply));
                await stream.FlushAsync();
                await Task.Delay(200);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or IOException)
            {
                Received.TrySetException(ex);
            }
        }

        internal static async Task<string> ReadHeadAsync(Stream stream)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (!(bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n'))
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    break;
                }

                bytes.Add(one[0]);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _serve;
            }
            catch (Exception)
            {
            }
        }
    }
}
