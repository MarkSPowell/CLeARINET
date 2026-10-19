using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Violens.ProxyCore.Certificates;
using Violens.ProxyHost.Client;
using Xunit;

namespace Violens.ProxyHost.Tests;

/// <summary>
/// The client and the host together, as an app would use them: the host
/// runs in this process on its own thread, connected to the client by a real
/// named pipe, with a test root certificate so no certificate store is
/// touched. Traffic is plain HTTP to a local server, sent through the proxy.
///
/// The host configures ProxyCore's process-wide identity, so these tests stay
/// in one class (xUnit runs a class's tests one at a time).
/// </summary>
public sealed class ProxyHostEndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private readonly string _dataFolder = Path.Combine(Path.GetTempPath(), "violens-proxyhost-" + Guid.NewGuid().ToString("N"));
    private readonly Origin _origin = new();
    private System.Security.Cryptography.X509Certificates.X509Certificate2 _root = null!;
    private NamedPipeServerStream _appPipe = null!;
    private Thread _hostThread = null!;
    private ProxyHostClient _client = null!;

    public async Task InitializeAsync()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        _root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1), "DO_NOT_TRUST_ProxyHostTestsRoot");

        var name = "vlpt-" + Guid.NewGuid().ToString("N")[..16];
        _appPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var hostPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepted = _appPipe.WaitForConnectionAsync();
        await hostPipe.ConnectAsync(10_000);
        await accepted;

        var server = new ProxyHostServer(hostPipe, "test-token", _root);
        _hostThread = new Thread(server.Run) { IsBackground = true, Name = "test host" };
        _hostThread.Start();

        _client = await ProxyHostClient.ConnectAsync(_appPipe, "test-token");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _hostThread.Join(TimeSpan.FromSeconds(10));
        await _origin.DisposeAsync();
        _root.Dispose();
        try
        {
            Directory.Delete(_dataFolder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task<StartResult> StartAsync(bool hookRequests = false, bool hookResponses = false, TimeSpan? hookTimeout = null) =>
        _client.StartAsync(
            new AppIdentity("ProxyHost Tests") { DataFolder = _dataFolder },
            new StartOptions
            {
                PreferredPort = 0,
                TrustRootCertificate = false,
                HookRequests = hookRequests,
                HookResponses = hookResponses,
                HookTimeout = hookTimeout ?? TimeSpan.FromSeconds(10),
            });

    private Task<(int Status, Dictionary<string, string> Headers, string Body)> GetAsync(int proxyPort, string path) =>
        SendThroughProxyAsync(proxyPort, $"GET http://127.0.0.1:{_origin.Port}{path} HTTP/1.1\r\nHost: 127.0.0.1:{_origin.Port}\r\n\r\n");

    [Fact]
    public async Task CapturesSessionsAndRunsRequestHooks()
    {
        var captured = new TaskCompletionSource<CapturedSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.SessionCaptured += (_, e) => captured.TrySetResult(e.Session);
        _client.OnBeforeRequest = context =>
        {
            context.Request.SetHeader("X-Hooked", "yes");
            context.Flags["ui-comments"] = "seen by the app";
        };

        var started = await StartAsync(hookRequests: true);
        Assert.True(started.Port > 0);
        Assert.Equal(_root.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert), started.RootCertificate);

        var response = await GetAsync(started.Port, "/hello?x=1");

        Assert.Equal(200, response.Status);
        Assert.Equal("hello", response.Body);
        Assert.Contains("X-Hooked: yes", await _origin.NextRequest.WaitAsync(Wait));

        var session = await captured.Task.WaitAsync(Wait);
        Assert.Equal($"http://127.0.0.1:{_origin.Port}/hello?x=1", session.Url);
        Assert.Equal("yes", session.Request.GetHeader("X-Hooked"));
        Assert.Equal("hello", Encoding.ASCII.GetString(session.Response.Body));
        Assert.Equal("seen by the app", session.Flags["ui-comments"]);
    }

    [Fact]
    public async Task ARequestHookCanAnswerWithoutTheServer()
    {
        _client.OnBeforeRequest = context => context.RespondWith(ProxyResponse.Text(200, "OK", "answered locally"));
        var started = await StartAsync(hookRequests: true);

        var response = await GetAsync(started.Port, "/never-sent");

        Assert.Equal("answered locally", response.Body);
        Assert.Equal(0, _origin.RequestCount);
    }

    [Fact]
    public async Task AResponseHookCanChangeTheResponse()
    {
        _client.OnBeforeResponse = context => context.Response!.SetBodyText("changed by the app");
        var started = await StartAsync(hookResponses: true);

        var response = await GetAsync(started.Port, "/change");

        Assert.Equal("changed by the app", response.Body);
    }

    [Fact]
    public async Task AHookThatTakesTooLongIsSkipped()
    {
        var logged = new ConcurrentQueue<string>();
        _client.Log += (_, e) => logged.Enqueue(e.Message);
        _client.OnBeforeRequest = context =>
        {
            Thread.Sleep(3000);
            context.Request.SetHeader("X-Too-Late", "yes");
        };
        var started = await StartAsync(hookRequests: true, hookTimeout: TimeSpan.FromMilliseconds(200));

        var response = await GetAsync(started.Port, "/slow-hook");

        Assert.Equal("hello", response.Body);
        Assert.DoesNotContain("X-Too-Late", await _origin.NextRequest.WaitAsync(Wait));
        // The log message travels over the pipe, the response over TCP: give it a moment.
        var deadline = DateTime.UtcNow + Wait;
        while (!logged.Any(message => message.Contains("didn't answer")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Contains(logged, message => message.Contains("didn't answer"));
    }

    [Fact]
    public async Task ABreakpointWaitsForTheAppAndTakesItsEdit()
    {
        var hit = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.BreakpointHit += (_, e) =>
        {
            hit.TrySetResult(e.RawText);
            e.Resume(e.RawText.Replace("\r\n\r\n", "\r\nX-Edited: by hand\r\n\r\n"));
        };
        await _client.SetBreakpointsAsync(new BreakpointSettings { RequestUrlContains = "/pause" });
        var started = await StartAsync();

        var response = await GetAsync(started.Port, "/pause-here");

        Assert.Equal(200, response.Status);
        Assert.StartsWith("GET ", await hit.Task.WaitAsync(Wait));
        Assert.Contains("X-Edited: by hand", await _origin.NextRequest.WaitAsync(Wait));
    }

    [Fact]
    public async Task SessionsCanBeSavedAndReadBackAsSaz()
    {
        var count = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _client.SessionCaptured += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                second.TrySetResult();
            }
        };
        var started = await StartAsync();
        await GetAsync(started.Port, "/saved");

        Directory.CreateDirectory(_dataFolder);
        var path = Path.Combine(_dataFolder, "capture.saz");
        Assert.Equal(1, await WaitForSaveAsync(path));

        await _client.ClearSessionsAsync();
        Assert.Equal(1, await _client.LoadSazAsync(path));
        await second.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task AFailedCommandSaysWhy()
    {
        var error = await Assert.ThrowsAsync<ProxyHostException>(() => _client.LoadSazAsync(Path.Combine(_dataFolder, "missing.saz")));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));

        // The host is still there afterwards.
        var started = await StartAsync();
        Assert.True(started.Port > 0);
    }

    [Fact]
    public async Task StartingTwiceIsRefused()
    {
        await StartAsync();

        await Assert.ThrowsAsync<ProxyHostException>(() => StartAsync());
    }

    [Fact]
    public async Task TheHostStopsWhenTheAppGoesAway()
    {
        await StartAsync();

        _client.Dispose();

        Assert.True(_hostThread.Join(TimeSpan.FromSeconds(10)), "The host should stop once the pipe closes.");
    }

    [Fact]
    public async Task AnotherProcessOnThePipeIsRefused()
    {
        var name = "vlpt-" + Guid.NewGuid().ToString("N")[..16];
        using var appPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var impostorPipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepted = appPipe.WaitForConnectionAsync();
        await impostorPipe.ConnectAsync(10_000);
        await accepted;
        var impostor = new Thread(new ProxyHostServer(impostorPipe, "some-other-token", _root).Run) { IsBackground = true };
        impostor.Start();

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProxyHostClient.ConnectAsync(appPipe, "the-real-token"));
        Assert.True(impostor.Join(TimeSpan.FromSeconds(10)));
    }

    /// <summary>The session may still be on its way into the host's store when the response arrives, so save until it's there.</summary>
    private async Task<int> WaitForSaveAsync(string path)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (true)
        {
            var saved = await _client.SaveSazAsync(path);
            if (saved > 0 || DateTime.UtcNow > deadline)
            {
                return saved;
            }

            await Task.Delay(50);
        }
    }

    private static async Task<(int Status, Dictionary<string, string> Headers, string Body)> SendThroughProxyAsync(int port, string request)
    {
        using var timeout = new CancellationTokenSource(Wait);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), timeout.Token);

        var head = await Origin.ReadHeadAsync(stream, timeout.Token);
        var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var status = int.Parse(lines[0].Split(' ')[1]);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var length = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value) : 0;
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, timeout.Token);
        return (status, headers, Encoding.UTF8.GetString(body));
    }

    /// <summary>A web server that answers every request with "hello" and remembers what it was sent.</summary>
    private sealed class Origin : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private readonly Channel _requests = new();
        private int _count;

        public Origin()
        {
            _listener.Start();
            _serve = ServeAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int RequestCount => Volatile.Read(ref _count);

        /// <summary>The head of the next request the server receives.</summary>
        public Task<string> NextRequest => _requests.NextAsync();

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        try
                        {
                            var stream = client.GetStream();
                            while (true)
                            {
                                var head = await ReadHeadAsync(stream, _stop.Token);
                                if (head.Length == 0)
                                {
                                    return;
                                }

                                Interlocked.Increment(ref _count);
                                _requests.Add(head);
                                await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray(), _stop.Token);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                        }
                    }
                });
            }
        }

        internal static async Task<string> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (!(bytes.Count >= 4 && bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n'))
            {
                if (await stream.ReadAsync(one, cancellationToken) == 0)
                {
                    break;
                }

                bytes.Add(one[0]);
            }

            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _serve;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Request heads in arrival order; each NextAsync takes the next one.</summary>
        private sealed class Channel
        {
            private readonly System.Threading.Channels.Channel<string> _inner = System.Threading.Channels.Channel.CreateUnbounded<string>();

            public void Add(string head) => _inner.Writer.TryWrite(head);

            public Task<string> NextAsync() => _inner.Reader.ReadAsync().AsTask();
        }
    }
}
