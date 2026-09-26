using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>
/// The listener end to end with an <see cref="IExtensionSessionHost"/>: a
/// real CONNECT tunnel, real TLS, and an extension that answers the request
/// itself. The host name is under <c>.invalid</c>, which never resolves, so
/// the test also proves no upstream connection is attempted.
/// </summary>
public class ExtensionSessionListenerTests
{
    private const string Host = "report-host.invalid";

    [Fact]
    public async Task AnExtensionCanAnswerARequestWithoutContactingTheServer()
    {
        // Made just now, as a fresh install's root would be; leaves must
        // still be issued for it (see LeafCertificateProvider).
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var root = CertificateAuthority.GenerateRoot(notBefore, notBefore.AddDays(1));
        var store = new SessionStore();
        var recorded = new TaskCompletionSource<Session>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.SessionAdded += session => recorded.TrySetResult(session);
        var extensions = new AnsweringHost();

        // The proxy reports connection failures on the console; capture it so
        // a failed handshake says why, not just "unexpected EOF".
        var proxyLog = new StringWriter();
        var originalConsole = Console.Out;
        Console.SetOut(TextWriter.Synchronized(proxyLog));

        var listener = InterceptingProxyListener.StartOnAvailablePort(
            0, new LeafCertificateProvider(root), store, sessionHost: extensions);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, listener.Port, timeout.Token);
            var stream = client.GetStream();

            await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {Host}:443 HTTP/1.1\r\nHost: {Host}:443\r\n\r\n"), timeout.Token);
            var connectReply = await ReadUntilBlankLineAsync(stream, timeout.Token);
            Assert.StartsWith("HTTP/1.1 200", connectReply);

            using var tls = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
            try
            {
                await tls.AuthenticateAsClientAsync(Host);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException)
            {
                await Task.Delay(500);
                throw new Xunit.Sdk.XunitException(
                    $"The TLS handshake with the proxy failed ({ex.Message}). The proxy logged:{Environment.NewLine}{proxyLog}");
            }

            await tls.WriteAsync(Encoding.ASCII.GetBytes(
                $"POST /report HTTP/1.1\r\nHost: {Host}\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{{}}"), timeout.Token);

            var response = await Http1MessageReader.ReadResponseAsync(tls, isResponseToHeadRequest: false, timeout.Token);

            Assert.NotNull(response);
            Assert.Equal(202, response!.StatusCode);
            Assert.Equal("received", Encoding.UTF8.GetString(response.Body));
            // Added by the response hook, which runs for an extension's own answer too.
            Assert.Contains(response.Headers, h => h.Name == "X-Seen-By-Response-Hook");

            var session = await recorded.Task.WaitAsync(timeout.Token);
            Assert.Equal("{}", Encoding.UTF8.GetString(session.Request.Body));
            Assert.Equal(202, session.Response.StatusCode);
            Assert.NotNull(session.Flags);
            Assert.Equal("answered locally", session.Flags!["ui-comments"]);

            Assert.Equal(
                new[] { "Begin https", "PeekAtRequestHeaders", "RequestBefore", "ResponseBefore", "ResponseAfter" },
                extensions.Calls.ToArray());
        }
        finally
        {
            listener.Stop();
            Console.SetOut(originalConsole);
        }
    }

    private static async Task<string> ReadUntilBlankLineAsync(Stream stream, CancellationToken cancellationToken)
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

    /// <summary>Answers every request itself, and records which hooks ran, in order.</summary>
    private sealed class AnsweringHost : IExtensionSessionHost
    {
        public List<string> Calls { get; } = [];

        public bool IsActive => true;

        public IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme)
        {
            lock (Calls)
            {
                Calls.Add($"Begin {scheme}");
            }

            return new AnsweringSession(this);
        }

        private sealed class AnsweringSession(AnsweringHost host) : IExtensionSession
        {
            private readonly Dictionary<string, string> _flags = new(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, string>? Flags => _flags.Count > 0 ? _flags : null;

            public void PeekAtRequestHeaders(CapturedRequest requestPreamble) => Record(nameof(PeekAtRequestHeaders));

            public ExtensionRequestResult RequestBefore(CapturedRequest request)
            {
                Record(nameof(RequestBefore));
                _flags["ui-comments"] = "answered locally";
                return new ExtensionRequestResult(
                    request,
                    new CapturedResponse("HTTP/1.1", 202, "Accepted", [("Content-Type", "text/plain")], Encoding.UTF8.GetBytes("received")));
            }

            public void RequestAfter(CapturedRequest request) => Record(nameof(RequestAfter));

            public void PeekAtResponseHeaders(CapturedRequest request, CapturedResponse responsePreamble) =>
                Record(nameof(PeekAtResponseHeaders));

            public CapturedResponse ResponseBefore(CapturedRequest request, CapturedResponse response)
            {
                Record(nameof(ResponseBefore));
                return response with { Headers = [.. response.Headers, ("X-Seen-By-Response-Hook", "yes")] };
            }

            public void ResponseAfter(CapturedRequest request, CapturedResponse response) => Record(nameof(ResponseAfter));

            private void Record(string call)
            {
                lock (host.Calls)
                {
                    host.Calls.Add(call);
                }
            }
        }
    }
}
