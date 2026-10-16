using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>Per-phase timings (<see cref="SessionTimings"/>): capturing them, and keeping them in HAR and SAZ files.</summary>
public class SessionTimingsTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static readonly SessionTimings Measured = new()
    {
        Blocked = 1.5,
        Dns = 12,
        Connect = 20,
        Ssl = 30,
        Send = 2,
        Wait = 150,
        Receive = 40,
        Paused = 0,
        ConnectionId = 7,
    };

    private static Session WithTimings(SessionTimings? timings)
    {
        var request = new CapturedRequest("GET", "/a", "HTTP/1.1", [("Host", "example.test")], []);
        var response = new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", "text/plain")], Encoding.ASCII.GetBytes("hello"));
        return new SessionStore().Add("example.test", new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero), request, response, null, "https", timings);
    }

    [Fact]
    public void TheTotalIsThePhasesThatApply()
    {
        Assert.Equal(255.5, Measured.Total);
        Assert.Equal(193.5, (Measured with { Dns = -1, Connect = -1, Ssl = -1 }).Total);
    }

    [Fact]
    public void AReusedConnectionHasNoConnectionPhases()
    {
        var recorder = new SessionTimingRecorder { ConnectionReused = true, ConnectionId = 3 };
        recorder.SendStarted();
        recorder.SendDone();
        recorder.FirstByte();
        recorder.Done();

        var timings = recorder.Build();

        Assert.Equal(SessionTimings.NotApplicable, timings.Dns);
        Assert.Equal(SessionTimings.NotApplicable, timings.Connect);
        Assert.Equal(SessionTimings.NotApplicable, timings.Ssl);
        Assert.True(timings.Send >= 0 && timings.Wait >= 0 && timings.Receive >= 0);
        Assert.True(timings.ConnectionReused);
        Assert.Equal(3, timings.ConnectionId);
        Assert.False(timings.AnsweredLocally);
    }

    [Fact]
    public void ANewConnectionHasEachPhase()
    {
        var recorder = new SessionTimingRecorder();
        recorder.ConnectStarted();
        recorder.DnsDone();
        recorder.Connected();
        recorder.TlsDone();
        recorder.SendStarted();
        recorder.SendDone();
        recorder.FirstByte();
        recorder.Done();

        var timings = recorder.Build();

        Assert.All(
            new[] { timings.Blocked, timings.Dns, timings.Connect, timings.Ssl, timings.Send, timings.Wait, timings.Receive },
            phase => Assert.True(phase >= 0));
    }

    [Fact]
    public void ALocalAnswerIsMarked()
    {
        var recorder = new SessionTimingRecorder();
        recorder.Done();

        var timings = recorder.Build(answeredLocally: true);

        Assert.True(timings.AnsweredLocally);
        Assert.Equal(SessionTimings.NotApplicable, timings.Dns);
        Assert.True(timings.Wait >= 0);
    }

    [Fact]
    public void AFailedConnectionCountsAsALocalAnswer()
    {
        var recorder = new SessionTimingRecorder();
        recorder.ConnectStarted();
        recorder.Done();

        Assert.True(recorder.Build().AnsweredLocally);
    }

    [Fact]
    public async Task TheListenerRecordsTimingsAndConnectionReuse()
    {
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1));
        await using var origin = ProxyAuthenticationTests.ScriptedProxy.Start(
            "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\none",
            "HTTP/1.1 200 OK\r\nContent-Length: 3\r\n\r\ntwo");
        var store = new SessionStore();
        var listener = InterceptingProxyListener.StartOnAvailablePort(0, new LeafCertificateProvider(root), store);
        try
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();
            foreach (var path in new[] { "/one", "/two" })
            {
                await stream.WriteAsync(
                    Encoding.ASCII.GetBytes($"GET http://127.0.0.1:{origin.Port}{path} HTTP/1.1\r\nHost: 127.0.0.1:{origin.Port}\r\n\r\n"), timeout.Token);
                Assert.NotNull(await Http1MessageReader.ReadResponseAsync(stream, isResponseToHeadRequest: false, timeout.Token));
            }

            await WaitForAsync(() => store.Snapshot().Count == 2, timeout.Token);
            var sessions = store.Snapshot();
            var first = sessions[0].Timings!;
            var second = sessions[1].Timings!;

            Assert.False(first.ConnectionReused);
            Assert.True(first.ConnectionId > 0);
            Assert.True(first.Dns >= 0 && first.Connect >= 0);
            Assert.Equal(SessionTimings.NotApplicable, first.Ssl); // plain HTTP
            Assert.True(first.Wait >= 0 && first.Receive >= 0);

            Assert.True(second.ConnectionReused);
            Assert.Equal(first.ConnectionId, second.ConnectionId);
            Assert.Equal(SessionTimings.NotApplicable, second.Connect);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task HarKeepsTheTimings()
    {
        using var buffer = new MemoryStream();
        HarWriter.Write(buffer, [WithTimings(Measured with { Paused = 5 })]);

        using (var har = JsonDocument.Parse(buffer.ToArray()))
        {
            var entry = har.RootElement.GetProperty("log").GetProperty("entries")[0];
            var timings = entry.GetProperty("timings");
            Assert.Equal(50, timings.GetProperty("connect").GetDouble()); // HAR counts TLS inside connect
            Assert.Equal(30, timings.GetProperty("ssl").GetDouble());
            Assert.Equal(5, timings.GetProperty("_paused").GetDouble());
            Assert.Equal(255.5, entry.GetProperty("time").GetDouble());
            Assert.Equal("7", entry.GetProperty("connection").GetString());
        }

        buffer.Position = 0;
        var store = new SessionStore();
        await HarReader.ImportAsync(buffer, store);

        var read = Assert.Single(store.Snapshot()).Timings!;
        Assert.Equal(Measured with { Paused = 5 }, read);
    }

    [Fact]
    public async Task ABrowsersHarTimingsAreRead()
    {
        // As Chrome writes them: connect includes ssl, -1 for what didn't happen.
        const string har = """
            {"log":{"version":"1.2","creator":{"name":"WebInspector","version":"537.36"},"entries":[
              {"startedDateTime":"2026-10-16T09:00:00.000Z","time":120.5,"connection":"4211",
               "request":{"method":"GET","url":"https://example.test/","httpVersion":"http/2.0","headers":[],"queryString":[],"cookies":[],"headersSize":-1,"bodySize":0},
               "response":{"status":200,"statusText":"","httpVersion":"http/2.0","headers":[],"cookies":[],"content":{"size":0,"mimeType":"text/html"},"redirectURL":"","headersSize":-1,"bodySize":0},
               "cache":{},"timings":{"blocked":3.2,"dns":-1,"ssl":20,"connect":35,"send":0.3,"wait":60,"receive":2}}]}}
            """;
        var store = new SessionStore();

        await HarReader.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(har)), store);

        var timings = Assert.Single(store.Snapshot()).Timings!;
        Assert.Equal(SessionTimings.NotApplicable, timings.Dns);
        Assert.Equal(15, timings.Connect);
        Assert.Equal(20, timings.Ssl);
        Assert.Equal(60, timings.Wait);
        Assert.Equal(4211, timings.ConnectionId);
    }

    [Fact]
    public async Task ZeroTimingsMeanNone()
    {
        using var buffer = new MemoryStream();
        HarWriter.Write(buffer, [WithTimings(null)]);
        buffer.Position = 0;
        var store = new SessionStore();

        await HarReader.ImportAsync(buffer, store);

        Assert.Null(Assert.Single(store.Snapshot()).Timings);
    }

    [Fact]
    public async Task SazKeepsTheTimingsToTheMillisecond()
    {
        using var zip = new MemoryStream();
        SazWriter.Write(zip, [WithTimings(Measured with { Blocked = 1 })]);
        zip.Position = 0;
        var store = new SessionStore();

        await SazReader.ImportAsync(zip, store);

        var timings = Assert.Single(store.Snapshot()).Timings!;
        Assert.Equal(1, timings.Blocked, 0);
        Assert.Equal(12, timings.Dns, 0);
        Assert.Equal(20, timings.Connect, 0);
        Assert.Equal(30, timings.Ssl, 0);
        Assert.Equal(2, timings.Send, 0);
        Assert.Equal(150, timings.Wait, 0);
        Assert.Equal(40, timings.Receive, 0);
    }

    [Fact]
    public async Task ASazWithoutTimingsReadsAsNone()
    {
        using var zip = new MemoryStream();
        SazWriter.Write(zip, [WithTimings(null)]);
        zip.Position = 0;
        var store = new SessionStore();

        await SazReader.ImportAsync(zip, store);

        Assert.Null(Assert.Single(store.Snapshot()).Timings);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(20, cancellationToken);
        }
    }
}
