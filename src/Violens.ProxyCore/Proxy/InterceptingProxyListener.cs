using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Violens.ProxyCore.AutoResponder;
using Violens.ProxyCore.Breakpoints;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Extensions;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Proxy.AutoConfig;
using Violens.ProxyCore.Scripting;
using Violens.ProxyCore.Sessions;
using Violens.ProxyCore.WebSockets;

namespace Violens.ProxyCore.Proxy;

/// <summary>
/// The intercepting proxy. Accepts CONNECT tunnels, terminating TLS with a
/// leaf certificate, and plain http:// proxy requests, and records each
/// request/response pair in the <see cref="SessionStore"/>. Bodies are
/// buffered only when a breakpoint, AutoResponder, FiddlerScript or an
/// extension will act on them; otherwise they're relayed as they arrive.
/// One request at a time per connection; no pipelining.
/// </summary>
public sealed class InterceptingProxyListener
{
    /// <summary>
    /// For AutoResponder rules that serve another URL. Never goes through the
    /// system proxy, which while capturing is VioLens itself.
    /// </summary>
    private readonly HttpClient _proxyUrlClient;

    private readonly LeafCertificateProvider _leafProvider;
    private readonly SessionStore _sessionStore;
    private readonly BreakpointManager _breakpointManager;
    private readonly AutoResponderRules _autoResponderRules;
    private readonly IScriptRunner? _scriptRunner;
    private readonly IExtensionAutoTamperHost? _extensionHost;
    private readonly IExtensionSessionHost? _sessionHost;
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;
    private UpstreamProxy? _gateway;
    private ProxyAutoConfig? _autoConfig;

    // Numbers the connections VioLens opens to servers and upstream
    // proxies, so the waterfall can show which requests shared one.
    private static int s_connectionCounter;
    private NetworkCredential? _proxyCredential;

    public InterceptingProxyListener(
        int port,
        LeafCertificateProvider leafProvider,
        SessionStore sessionStore,
        BreakpointManager? breakpointManager = null,
        AutoResponderRules? autoResponderRules = null,
        IScriptRunner? scriptRunner = null,
        IExtensionAutoTamperHost? extensionHost = null,
        IExtensionSessionHost? sessionHost = null,
        bool allowRemoteClients = false)
    {
        _leafProvider = leafProvider;
        _sessionStore = sessionStore;
        _proxyUrlClient = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new UpstreamWebProxy(this),
            UseProxy = true,
            AutomaticDecompression = DecompressionMethods.None,
        });
        // With nothing configured, each check is a single boolean.
        _breakpointManager = breakpointManager ?? new BreakpointManager();
        _autoResponderRules = autoResponderRules ?? new AutoResponderRules();
        // Null means no script loaded.
        _scriptRunner = scriptRunner;
        // Null means no extensions loaded.
        _extensionHost = extensionHost;
        // Ported Fiddler-shaped extensions (see IExtensionSessionHost). Null,
        // or IsActive false, means none are loaded.
        _sessionHost = sessionHost;
        AllowsRemoteClients = allowRemoteClients;
        _listener = new TcpListener(allowRemoteClients ? IPAddress.Any : IPAddress.Loopback, port);
    }

    /// <summary>The port being listened on, which may differ from the one requested. Valid after <see cref="Start"/>.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>
    /// Whether this listener accepts connections from other computers
    /// (phones, other machines on the network) as well as this one: Fiddler
    /// Classic's "Allow remote computers to connect". Fixed when constructed.
    /// </summary>
    public bool AllowsRemoteClients { get; }

    /// <summary>
    /// The proxy VioLens forwards its own outgoing connections through, or
    /// null to connect directly. Read when each new client connection opens
    /// its connection to the server, so it can change while running. See
    /// <see cref="UpstreamProxy"/>.
    /// </summary>
    public UpstreamProxy? Gateway
    {
        get => Volatile.Read(ref _gateway);
        set => Volatile.Write(ref _gateway, value);
    }

    /// <summary>
    /// Automatic proxy configuration (a PAC script): when set and
    /// <see cref="Gateway"/> isn't, each server connection asks it which
    /// proxy to use, trying the next one it lists when one can't be reached.
    /// Read when each server connection is opened, like <see cref="Gateway"/>.
    /// The listener doesn't own it; whoever sets it disposes it.
    /// </summary>
    public ProxyAutoConfig? AutoConfig
    {
        get => Volatile.Read(ref _autoConfig);
        set => Volatile.Write(ref _autoConfig, value);
    }

    /// <summary>
    /// The user name and password for an upstream proxy that asks for a
    /// sign-in (see <see cref="ProxyAuthenticator"/>), or null to sign in as
    /// the signed-in account (Windows sign-in: Negotiate or NTLM). Read
    /// each time a sign-in is needed.
    /// </summary>
    public NetworkCredential? ProxyCredential
    {
        get => Volatile.Read(ref _proxyCredential);
        set => Volatile.Write(ref _proxyCredential, value);
    }

    private long _maxCapturedBodyBytes = Http1MessageReader.DefaultMaxCapturedBodyBytes;

    /// <summary>
    /// How much of each relayed request or response body is kept in its
    /// session (64 MB unless changed). Every byte is still relayed; a body
    /// larger than this is captured only up to here, and the session gets
    /// <see cref="Session.RequestBodyTruncatedFlag"/> or
    /// <see cref="Session.ResponseBodyTruncatedFlag"/>. Read when each
    /// client connection opens. A body that's buffered so a breakpoint,
    /// FiddlerScript or an extension can change it is always kept whole.
    /// </summary>
    public long MaxCapturedBodyBytes
    {
        get => Interlocked.Read(ref _maxCapturedBodyBytes);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Interlocked.Exchange(ref _maxCapturedBodyBytes, value);
        }
    }

    private static readonly IReadOnlyList<UpstreamProxy?> DirectOnly = [null];

    /// <summary>
    /// The routes to try for a server connection, in order: a proxy, or
    /// null for direct. This machine is always reached directly.
    /// </summary>
    private async Task<IReadOnlyList<UpstreamProxy?>> ChooseRoutesAsync(
        bool isTls, string host, int port, string target, CancellationToken cancellationToken)
    {
        if (UpstreamProxy.IsLoopback(host))
        {
            return DirectOnly;
        }

        if (Gateway is { } gateway)
        {
            return gateway.ShouldBypass(host) ? DirectOnly : [gateway];
        }

        if (AutoConfig is not { } autoConfig)
        {
            return DirectOnly;
        }

        // Browsers give a PAC script only the scheme, host and port of an
        // HTTPS request (the path is private), and the whole URL for HTTP.
        var authority = (host.Contains(':') ? $"[{host}]" : host) + (port == (isTls ? 443 : 80) ? string.Empty : $":{port}");
        var url = isTls ? $"https://{authority}/" : $"http://{authority}{target}";
        return await autoConfig.GetRoutesAsync(url, host, cancellationToken);
    }

    /// <summary>
    /// Starts on <paramref name="preferredPort"/>, or on any free port if it's
    /// taken (usually by another VioLens). Read the result from <see cref="Port"/>.
    /// </summary>
    public static InterceptingProxyListener StartOnAvailablePort(
        int preferredPort,
        LeafCertificateProvider leafProvider,
        SessionStore sessionStore,
        BreakpointManager? breakpointManager = null,
        AutoResponderRules? autoResponderRules = null,
        IScriptRunner? scriptRunner = null,
        IExtensionAutoTamperHost? extensionHost = null,
        IExtensionSessionHost? sessionHost = null,
        bool allowRemoteClients = false)
    {
        var preferred = new InterceptingProxyListener(preferredPort, leafProvider, sessionStore, breakpointManager, autoResponderRules, scriptRunner, extensionHost, sessionHost, allowRemoteClients);
        try
        {
            preferred.Start();
            return preferred;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // Port 0: the OS picks a free port.
            var fallback = new InterceptingProxyListener(0, leafProvider, sessionStore, breakpointManager, autoResponderRules, scriptRunner, extensionHost, sessionHost, allowRemoteClients);
            fallback.Start();
            return fallback;
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    /// <summary>
    /// The routes <see cref="ChooseRoutesAsync"/> picks, as an
    /// <see cref="IWebProxy"/> for <see cref="HttpClient"/>: the first one
    /// (the gateway, the PAC script's first choice, or direct). Credentials
    /// are the proxy sign-in (<see cref="ProxyCredential"/>), or the
    /// signed-in account.
    /// </summary>
    private sealed class UpstreamWebProxy(InterceptingProxyListener listener) : IWebProxy
    {
        public ICredentials? Credentials
        {
            get => (ICredentials?)listener.ProxyCredential ?? CredentialCache.DefaultCredentials;
            set { }
        }

        public Uri? GetProxy(Uri destination) =>
            Route(destination) is { } proxy ? new Uri($"http://{proxy}/") : destination;

        public bool IsBypassed(Uri host) => Route(host) is null;

        private UpstreamProxy? Route(Uri destination)
        {
            var isTls = string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var routes = listener.ChooseRoutesAsync(isTls, destination.IdnHost, destination.Port, destination.PathAndQuery, CancellationToken.None)
                .GetAwaiter().GetResult();
            return routes.Count > 0 ? routes[0] : null;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        client.NoDelay = true;

        try
        {
            // Read-buffered (see ReadBufferedStream), and the TLS handshake after
            // CONNECT reads through it too, so nothing read ahead is lost.
            using var clientStream = new ReadBufferedStream(client.GetStream());
            var first = await Http1MessageReader.ReadRequestPreambleAsync(clientStream, cancellationToken);
            if (first is null)
            {
                return;
            }

            if (!string.Equals(first.Method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                // Plain HTTP. A request addressed to VioLens itself gets its
                // home page and root certificate (how a phone installs it);
                // an http:// request is captured and forwarded like HTTPS
                // traffic; anything else (an ftp:// URL, say) is answered
                // rather than silently dropped.
                if (ProxyHomePage.IsRequestForProxy(first.Target, Port))
                {
                    await AnswerAsync(clientStream, first, ProxyHomePage.Build(first.Target, _leafProvider.IssuerCertificateDer), cancellationToken);
                    return;
                }

                if (!first.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    await AnswerAsync(clientStream, first, ProxyHomePage.NotForwarded(first.Method, first.Target), cancellationToken);
                    return;
                }

                await PumpSessionsAsync(
                    isTls: false, string.Empty, PlainHttpRequest.DefaultPort, client, clientStream, first, _sessionStore,
                    _breakpointManager, _autoResponderRules, _scriptRunner, _extensionHost, _sessionHost, ChooseRoutesAsync, () => ProxyCredential, _proxyUrlClient, Port,
                    () => _leafProvider.IssuerCertificateDer, MaxCapturedBodyBytes, cancellationToken);
                return;
            }

            var (targetHost, targetPort) = ParseConnectTarget(first.Target);

            var established = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
            await clientStream.WriteAsync(established, cancellationToken);

            using var clientTls = new SslStream(clientStream, leaveInnerStreamOpen: false);
            var serverOptions = new SslServerAuthenticationOptions
            {
                ServerCertificateSelectionCallback = (_, sniHostName) =>
                    _leafProvider.GetCertificateFor(string.IsNullOrEmpty(sniHostName) ? targetHost : sniHostName),
                // Let the OS choose TLS 1.2 or 1.3.
                EnabledSslProtocols = SslProtocols.None,
            };
            await clientTls.AuthenticateAsServerAsync(serverOptions, cancellationToken);

            // The server connection is opened later, only when needed.
            await PumpSessionsAsync(
                isTls: true, targetHost, targetPort, client, new ReadBufferedStream(clientTls), firstRequest: null, _sessionStore, _breakpointManager,
                _autoResponderRules, _scriptRunner, _extensionHost, _sessionHost, ChooseRoutesAsync, () => ProxyCredential, _proxyUrlClient, Port,
                () => _leafProvider.IssuerCertificateDer, MaxCapturedBodyBytes, cancellationToken);
        }
        catch (BreakpointAbortedException ex)
        {
            ProxyEngine.Log("proxy", ex.Message);
        }
        catch (Exception ex)
        {
            ProxyEngine.Log("proxy", $"Connection ended: {DescribeException(ex)}");
        }
    }

    /// <summary>Drains a plain request's body, sends VioLens's own answer, and logs it.</summary>
    private static async Task AnswerAsync(Stream clientStream, CapturedRequest request, CapturedResponse answer, CancellationToken cancellationToken)
    {
        await Http1MessageReader.ReadBodyAsync(clientStream, request.Headers, cancellationToken);
        await HttpMessageWriter.WriteResponseAsync(clientStream, answer, cancellationToken);
        ProxyEngine.Log("proxy", $"{request.Method} {request.Target} -> {answer.StatusCode} (answered by {ProxyEngine.Identity.ProductName})");
    }

    private static string DescribeException(Exception ex)
    {
        // The useful reason is in the inner exceptions.
        var description = $"{ex.GetType().Name}: {ex.Message}";
        var inner = ex.InnerException;
        while (inner is not null)
        {
            description += $" ---> {inner.GetType().Name}: {inner.Message}";
            inner = inner.InnerException;
        }

        return description;
    }

    /// <summary>
    /// Handles the requests on one connection. The server connection is opened
    /// only when a request actually needs it, so AutoResponder can answer every
    /// request with no route to the server at all.
    ///
    /// Per request, before it's forwarded: FiddlerScript's OnBeforeRequest,
    /// then VioLens extensions' AutoTamperRequestBefore, then ported
    /// extensions', then the request breakpoint. Fiddler runs the script before
    /// breakpoints; the extension order is assumed. Responses follow the same
    /// order. Requests AutoResponder answers skip all of these. The After hooks
    /// run once each message is sent, whether buffered or relayed.
    /// </summary>
    private static async Task PumpSessionsAsync(
        bool isTls,
        string targetHost,
        int targetPort,
        TcpClient clientConnection,
        Stream clientTls,
        CapturedRequest? firstRequest,
        SessionStore sessionStore,
        BreakpointManager breakpointManager,
        AutoResponderRules autoResponderRules,
        IScriptRunner? scriptRunner,
        IExtensionAutoTamperHost? extensionHost,
        IExtensionSessionHost? sessionHost,
        Func<bool, string, int, string, CancellationToken, Task<IReadOnlyList<UpstreamProxy?>>> chooseRoutes,
        Func<NetworkCredential?> proxyCredential,
        HttpClient proxyUrlClient,
        int ownPort,
        Func<byte[]> rootCertificateDer,
        long maxCapturedBodyBytes,
        CancellationToken cancellationToken)
    {
        // clientTls is the TLS stream for a CONNECT tunnel, or the plain
        // connection for http:// requests. A plain connection can carry
        // requests for different servers, so the target is set per request.
        var scheme = isTls ? "https" : "http";
        TcpClient? upstreamClient = null;
        Stream? upstreamTls = null;
        string? upstreamKey = null;
        var sendAbsoluteForm = false;
        UpstreamProxy? upstreamRoute = null;
        var upstreamConnectionId = 0;
        UpstreamTlsProbe? upstreamTlsProbe = null;
        SessionTimingRecorder? currentTiming = null;
        var pendingRequest = firstRequest;

        // Opened the first time a request has to reach the server, then reused.
        async Task<Stream> EnsureUpstreamAsync(string target)
        {
            var key = $"{targetHost}:{targetPort}";
            if (upstreamTls is not null && upstreamKey != key)
            {
                // A plain HTTP client moved on to a different server.
                upstreamTls.Dispose();
                upstreamClient?.Dispose();
                upstreamTls = null;
                upstreamClient = null;
            }

            if (upstreamTls is not null && currentTiming is not null)
            {
                currentTiming.ConnectionReused = true;
                currentTiming.ConnectionId = upstreamConnectionId;
                currentTiming.Proxy = upstreamRoute?.ToString();
                currentTiming.TlsProbe = upstreamTlsProbe;
            }

            if (upstreamTls is null)
            {
                // Trying the next route when one can't be reached, as browsers do.
                upstreamClient?.Dispose(); // left over from a failed TLS handshake
                upstreamClient = null;
                upstreamTlsProbe = null;
                var routes = await chooseRoutes(isTls, targetHost, targetPort, target, cancellationToken);
                currentTiming?.ConnectStarted();
                UpstreamProxy? route = null;
                for (var i = 0; upstreamClient is null; i++)
                {
                    route = routes[i];
                    try
                    {
                        upstreamClient = await ConnectAsync(route);
                    }
                    catch (SocketException ex) when (i + 1 < routes.Count)
                    {
                        ProxyEngine.Log("proxy",
                            $"{targetHost}: couldn't reach {DescribeRoute(route)} ({ex.Message}); trying {DescribeRoute(routes[i + 1])}.");
                    }
                }

                currentTiming?.Connected();
                upstreamConnectionId = Interlocked.Increment(ref s_connectionCounter);
                if (currentTiming is not null)
                {
                    currentTiming.ConnectionId = upstreamConnectionId;
                    currentTiming.Proxy = route?.ToString();
                }

                if (isTls)
                {
                    var tls = new SslStream(upstreamClient!.GetStream(), leaveInnerStreamOpen: false);

                    // The probe records a client-certificate request on the session.
                    // Default certificate validation: a real upstream certificate problem
                    // should show, not be hidden.
                    upstreamTlsProbe = new UpstreamTlsProbe();
                    upstreamTlsProbe.Attach(tls);
                    if (currentTiming is not null)
                    {
                        currentTiming.TlsProbe = upstreamTlsProbe;
                    }

                    await tls.AuthenticateAsClientAsync(upstreamTlsProbe.CreateOptions(targetHost), cancellationToken);
                    currentTiming?.TlsDone();
                    upstreamTls = new ReadBufferedStream(tls);
                    sendAbsoluteForm = false;
                }
                else
                {
                    // Plain HTTP goes to a proxy as an ordinary proxy
                    // request (full URL in the request line).
                    upstreamTls = new ReadBufferedStream(upstreamClient!.GetStream());
                    sendAbsoluteForm = route is not null;
                }

                upstreamRoute = route;

                upstreamKey = key;
            }

            return upstreamTls;
        }

        // One route: a CONNECT tunnel through a proxy for HTTPS, a plain
        // connection to the proxy for HTTP, or straight to the server.
        async Task<TcpClient> ConnectAsync(UpstreamProxy? route)
        {
            if (route is not null && isTls)
            {
                return await route.ConnectTunnelAsync(targetHost, targetPort, proxyCredential(), cancellationToken);
            }

            var client = new TcpClient();
            try
            {
                // The address is looked up first, on its own, so the
                // waterfall can show DNS time apart from connecting.
                var (host, port) = route is null ? (targetHost, targetPort) : (route.Host, route.Port);
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                currentTiming?.DnsDone();
                await client.ConnectAsync(addresses, port, cancellationToken);
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        // A plain HTTP request sent through a proxy that answered 407:
        // sign in (see ProxyAuthenticator) and send the request again, on
        // the same connection when the proxy keeps it open. Returns the
        // answer to pass on: the real response, or the last 407 when
        // signing in didn't work. Null if the proxy closed the connection.
        async Task<CapturedResponse?> SignInToProxyAsync(CapturedRequest request, CapturedResponse challenge)
        {
            var proxy = upstreamRoute!;
            var credential = proxyCredential();
            using var authenticator = new ProxyAuthenticator(proxy.Host, credential);
            var reply = challenge;
            while (reply.StatusCode == 407)
            {
                var authorization = authenticator.Respond(reply.Headers);
                if (authorization is null)
                {
                    ProxyEngine.Log("proxy", $"{proxy}: {UpstreamProxy.SignInFailedMessage(authenticator, credential)}");
                    return reply;
                }

                if (UpstreamProxy.KeepsConnection(reply))
                {
                    await Http1MessageReader.ReadBodyAsync(upstreamTls!, reply.Headers, cancellationToken);
                }
                else
                {
                    upstreamTls?.Dispose();
                    upstreamClient?.Dispose();
                    upstreamTls = null;
                    upstreamClient = new TcpClient();
                    await upstreamClient.ConnectAsync(proxy.Host, proxy.Port, cancellationToken);
                    upstreamTls = new ReadBufferedStream(upstreamClient.GetStream());
                }

                var stream = upstreamTls!;
                var outgoing = ForUpstream(request);
                outgoing = outgoing with { Headers = [.. outgoing.Headers, ("Proxy-Authorization", authorization)] };
                await HttpMessageWriter.WriteRequestAsync(stream, outgoing, cancellationToken);
                var next = await Http1MessageReader.ReadResponsePreambleAsync(stream, cancellationToken);
                if (next is null)
                {
                    return null;
                }

                reply = next;
            }

            return reply;
        }

        // What actually goes to the server (or the network's proxy) for a
        // request: unchanged for HTTPS; for plain HTTP, without the
        // proxy-only headers, and with the full URL when going via a proxy.
        CapturedRequest ForUpstream(CapturedRequest outgoing) =>
            isTls ? outgoing : PlainHttpRequest.ForUpstream(outgoing, sendAbsoluteForm);

        // For plain HTTP, the server a request goes to comes from its Host
        // header, which a script, extension or breakpoint may have changed.
        void RouteFrom(CapturedRequest outgoing)
        {
            if (!isTls && PlainHttpRequest.TryGetRoute(outgoing.Headers, out var host, out var port))
            {
                targetHost = host;
                targetPort = port;
            }
        }

        // Opens the server connection, or answers 502 Bad Gateway, records it,
        // and returns null.
        async Task<Stream?> TryEnsureUpstreamAsync(CapturedRequest outgoing, DateTimeOffset startedAt)
        {
            try
            {
                return await EnsureUpstreamAsync(outgoing.Target);
            }
            catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
            {
                var askedForCertificate = upstreamTlsProbe?.ClientCertificateRequested == true;
                var failure = BadGateway(
                    targetHost,
                    targetPort,
                    ex,
                    askedForCertificate
                        ? $"The server asked for a client certificate (mutual TLS), and {ProxyEngine.Identity.ProductName} doesn't send one yet, so the server may have refused the connection for that reason."
                        : null);
                await HttpMessageWriter.WriteResponseAsync(clientTls, failure, cancellationToken);
                currentTiming?.Done();
                var failedTimings = currentTiming?.Build();
                var failed = sessionStore.Add(
                    targetHost, startedAt, outgoing, failure, WithTlsFlags(null, failedTimings), scheme, failedTimings);
                ProxyEngine.Log("session",
                    $"#{failed.Id} 502 {outgoing.Method} {scheme}://{targetHost}{outgoing.Target} -- {ex.Message}");
                return null;
            }
        }

        try
        {
            while (true)
            {
                var requestPreamble = pendingRequest ?? await Http1MessageReader.ReadRequestPreambleAsync(clientTls, cancellationToken);
                pendingRequest = null;
                if (requestPreamble is null)
                {
                    // The client closed the connection between requests.
                    return;
                }

                // Timed from the request's arrival, not keep-alive idle time.
                var startedAt = DateTimeOffset.Now;
                var timing = new SessionTimingRecorder();
                currentTiming = timing;

                if (!isTls)
                {
                    // Plain HTTP is stored like HTTPS: a path-only target plus a Host header
                    // (see PlainHttpRequest).
                    if (ProxyHomePage.IsRequestForProxy(requestPreamble.Target, ownPort) && !requestPreamble.Target.StartsWith('/'))
                    {
                        await AnswerAsync(clientTls, requestPreamble, ProxyHomePage.Build(requestPreamble.Target, rootCertificateDer()), cancellationToken);
                        return;
                    }

                    if (!PlainHttpRequest.TryNormalize(requestPreamble, out var normalized, out var plainHost, out var plainPort))
                    {
                        await AnswerAsync(clientTls, requestPreamble, ProxyHomePage.NotForwarded(requestPreamble.Method, requestPreamble.Target), cancellationToken);
                        return;
                    }

                    requestPreamble = normalized;
                    targetHost = plainHost;
                    targetPort = plainPort;
                }

                // AutoResponder only looks at the method and URL, so it can decide from
                // the preamble.
                var url = isTls
                    ? $"https://{targetHost}{requestPreamble.Target}"
                    : $"http://{ContentDecoder.FindHeader(requestPreamble.Headers, "Host") ?? targetHost}{requestPreamble.Target}";
                var outcome = autoResponderRules.AnyActive
                    ? autoResponderRules.Evaluate(requestPreamble.Method, url)
                    : AutoResponderOutcome.PassThrough;

                if (outcome.DelayMilliseconds > 0)
                {
                    // Applies whether the request is answered locally or forwarded.
                    await Task.Delay(outcome.DelayMilliseconds, cancellationToken);
                }

                var isAnsweredLocally = outcome.FinalActionKind is
                    AutoResponderActionKind.ServeFile or AutoResponderActionKind.ProxyUrl or AutoResponderActionKind.Redirect or
                    AutoResponderActionKind.Reset or AutoResponderActionKind.Drop or AutoResponderActionKind.CorsPreflightAllow;

                if (isAnsweredLocally)
                {
                    // Answered locally, so there's nothing to relay.
                    var localRequestBody = await Http1MessageReader.ReadBodyAsync(clientTls, requestPreamble.Headers, cancellationToken);
                    var localRequest = requestPreamble with { Body = localRequestBody };

                    if (outcome.FinalActionKind == AutoResponderActionKind.Reset)
                    {
                        RecordAutoRespondedSession(sessionStore, targetHost, scheme, startedAt, localRequest, "Connection Reset (AutoResponder *reset)", timing.Build(answeredLocally: true));
                        // A TCP RST needs a zero linger time.
                        clientConnection.Client.LingerState = new LingerOption(true, 0);
                        clientConnection.Close();
                        return;
                    }

                    if (outcome.FinalActionKind == AutoResponderActionKind.Drop)
                    {
                        RecordAutoRespondedSession(sessionStore, targetHost, scheme, startedAt, localRequest, "Connection Dropped (AutoResponder *drop)", timing.Build(answeredLocally: true));
                        // No response; the connection just closes.
                        return;
                    }

                    var localResponse = await BuildMockResponseAsync(outcome, localRequest, proxyUrlClient, cancellationToken);
                    await HttpMessageWriter.WriteResponseAsync(clientTls, localResponse, cancellationToken);
                    timing.Done();
                    var localSession = sessionStore.Add(targetHost, startedAt, localRequest, localResponse, flags: null, scheme, timing.Build(answeredLocally: true));
                    ProxyEngine.Log("session",
                        $"#{localSession.Id} {localResponse.StatusCode} {localRequest.Method} {url} " +
                        $"(AutoResponder: {outcome.FinalActionKind})");
                    continue;
                }

                // Not answered by AutoResponder. The server connection waits until no
                // ported extension answers the request either.
                var outgoingPreamble = outcome.HeadersToSet.Count > 0
                    ? requestPreamble with { Headers = MergeHeaders(requestPreamble.Headers, outcome.HeadersToSet) }
                    : requestPreamble;

                // Ported extensions get one session object for the whole request (see
                // IExtensionSessionHost).
                var extensionSession = sessionHost is { IsActive: true }
                    ? sessionHost.BeginSession(sessionStore.PeekNextId(), targetHost, scheme)
                    : null;
                extensionSession?.PeekAtRequestHeaders(outgoingPreamble);

                // Breakpoint rules only look at the method and target, so this is
                // decided before reading the body: buffer it only if something will
                // act on it, otherwise relay it.
                var mustBufferRequestForScript = scriptRunner?.HasOnBeforeRequest ?? false;
                var mustBufferRequestForExtensions = extensionHost?.HasAnyRequestBeforeHandlers ?? false;
                CapturedRequest request;
                CapturedResponse? extensionLocalResponse = null;
                Http1MessageReader.RelayedBody? relayedRequestBody = null;
                Http1MessageReader.RelayedBody? relayedResponseBody = null;
                if (breakpointManager.WouldBreakBeforeRequest(outgoingPreamble) || outcome.ForceBreakpointBeforeRequest ||
                    mustBufferRequestForScript || mustBufferRequestForExtensions || extensionSession is not null)
                {
                    var requestBody = await Http1MessageReader.ReadBodyAsync(clientTls, outgoingPreamble.Headers, cancellationToken);
                    request = outgoingPreamble with { Body = requestBody };

                    if (mustBufferRequestForScript)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        request = scriptRunner!.RunOnBeforeRequest(peekedSessionId, targetHost, request).Request;
                    }

                    if (mustBufferRequestForExtensions)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        request = extensionHost!.RunRequestBefore(peekedSessionId, targetHost, request);
                    }

                    // Ported extensions may answer the request themselves, in which case it
                    // never reaches the server or a breakpoint.
                    if (extensionSession is not null)
                    {
                        var result = extensionSession.RequestBefore(request);
                        request = result.Request;
                        extensionLocalResponse = result.LocalResponse;
                    }

                    if (extensionLocalResponse is null)
                    {
                        // May pause here for editing.
                        var requestPauseStart = SessionTimingRecorder.Now();
                        request = await breakpointManager.ApplyRequestBreakpointAsync(targetHost, request, cancellationToken);
                        timing.AddPause(requestPauseStart, SessionTimingRecorder.Now());
                        RouteFrom(request);
                        var bufferedUpstream = await TryEnsureUpstreamAsync(request, startedAt);
                        if (bufferedUpstream is null)
                        {
                            return;
                        }

                        timing.SendStarted();
                        await HttpMessageWriter.WriteRequestAsync(bufferedUpstream, ForUpstream(request), cancellationToken);
                        timing.SendDone();
                    }
                }
                else
                {
                    RouteFrom(outgoingPreamble);
                    var upstream = await TryEnsureUpstreamAsync(outgoingPreamble, startedAt);
                    if (upstream is null)
                    {
                        return;
                    }

                    timing.SendStarted();
                    await HttpMessageWriter.WriteRequestPreambleAsync(upstream, ForUpstream(outgoingPreamble), cancellationToken);
                    var requestBody = await Http1MessageReader.RelayBodyAsync(
                        clientTls, upstream, outgoingPreamble.Headers, cancellationToken, maxCapturedBodyBytes);
                    timing.SendDone();
                    request = outgoingPreamble with { Body = requestBody.Captured };
                    relayedRequestBody = requestBody;
                }

                if (extensionLocalResponse is not null)
                {
                    // An extension answered (utilCreateResponseAndBypassServer). Nothing was
                    // sent, so the request-after hooks don't run; the response hooks do.
                    var localResponse = extensionSession!.ResponseBefore(request, extensionLocalResponse);
                    await HttpMessageWriter.WriteResponseAsync(clientTls, localResponse, cancellationToken);
                    extensionSession.ResponseAfter(request, localResponse);

                    timing.Done();
                    var answeredSession = sessionStore.Add(
                        targetHost, startedAt, request, localResponse, extensionSession.Flags, scheme, timing.Build(answeredLocally: true));
                    ProxyEngine.Log("session",
                        $"#{answeredSession.Id} {localResponse.StatusCode} {request.Method} {url} (answered by an extension)");
                    continue;
                }

                extensionHost?.RunRequestAfter(sessionStore.PeekNextId(), targetHost, request);
                extensionSession?.RequestAfter(request);

                // Every path above that didn't return has sent the request
                // upstream, so the connection exists.
                var upstreamStream = upstreamTls!;

                var isHeadRequest = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
                var responsePreamble = await Http1MessageReader.ReadResponsePreambleAsync(upstreamStream, cancellationToken);
                timing.FirstByte();

                // Interim responses (100 Continue, 102 Processing, 103 Early
                // Hints) come before the real one, on the same request. Pass
                // each on and keep reading: treating one as the answer would
                // leave the real response unread while the client waits for
                // it. 101 is final -- the connection switches protocols.
                while (responsePreamble is { StatusCode: >= 100 and < 200 and not 101 } interim)
                {
                    await HttpMessageWriter.WriteResponsePreambleAsync(clientTls, interim, cancellationToken);
                    responsePreamble = await Http1MessageReader.ReadResponsePreambleAsync(upstreamStream, cancellationToken);
                }

                if (responsePreamble is { StatusCode: 407 } && !isTls && upstreamRoute is not null)
                {
                    // The network's proxy wants a sign-in for plain HTTP.
                    // (For HTTPS that happened when the tunnel was opened.)
                    responsePreamble = await SignInToProxyAsync(request, responsePreamble);
                    upstreamStream = upstreamTls!;
                }

                if (responsePreamble is null)
                {
                    ProxyEngine.Log("proxy",
                        $"{targetHost}: upstream closed the connection before answering {request.Method} {request.Target}.");
                    return;
                }

                var responseHasNoBody = Http1MessageReader.ResponseHasNoBody(responsePreamble.StatusCode, isHeadRequest);
                extensionSession?.PeekAtResponseHeaders(request, responsePreamble);

                // Same fork as the request side above, for OnBeforeResponse.
                var mustBufferResponseForScript = scriptRunner?.HasOnBeforeResponse ?? false;
                var mustBufferResponseForExtensions = extensionHost?.HasAnyResponseBeforeHandlers ?? false;
                CapturedResponse response;
                if (breakpointManager.WouldBreakBeforeResponse(request, responsePreamble) || outcome.ForceBreakpointAfterResponse ||
                    mustBufferResponseForScript || mustBufferResponseForExtensions || extensionSession is not null)
                {
                    var responseBody = responseHasNoBody
                        ? Array.Empty<byte>()
                        : await Http1MessageReader.ReadResponseBodyAsync(upstreamStream, responsePreamble.Headers, cancellationToken);
                    response = responsePreamble with { Body = responseBody };

                    if (mustBufferResponseForScript)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        response = scriptRunner!.RunOnBeforeResponse(peekedSessionId, targetHost, request, response).Response;
                    }

                    if (mustBufferResponseForExtensions)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        response = extensionHost!.RunResponseBefore(peekedSessionId, targetHost, request, response);
                    }

                    // Ported extensions' AutoTamperResponseBefore, on the
                    // same session object as their request hooks.
                    if (extensionSession is not null)
                    {
                        response = extensionSession.ResponseBefore(request, response);
                    }

                    var responsePauseStart = SessionTimingRecorder.Now();
                    response = await breakpointManager.ApplyResponseBreakpointAsync(
                        targetHost, request, response, cancellationToken);
                    timing.AddPause(responsePauseStart, SessionTimingRecorder.Now());
                    await HttpMessageWriter.WriteResponseAsync(clientTls, response, cancellationToken);
                }
                else
                {
                    // Relayed live, so a streamed response reaches the client as it's
                    // generated.
                    await HttpMessageWriter.WriteResponsePreambleAsync(clientTls, responsePreamble, cancellationToken);
                    var responseBody = responseHasNoBody
                        ? Http1MessageReader.RelayedBody.Empty
                        : await Http1MessageReader.RelayResponseBodyAsync(
                            upstreamStream, clientTls, responsePreamble.Headers, cancellationToken, maxCapturedBodyBytes);
                    response = responsePreamble with { Body = responseBody.Captured };
                    relayedResponseBody = responseBody;
                }

                extensionHost?.RunResponseAfter(sessionStore.PeekNextId(), targetHost, request, response);
                extensionSession?.ResponseAfter(request, response);

                timing.Done();
                var timings = timing.Build();

                // A WebSocket handshake: the session gets a log its messages
                // are added to as they pass (see the 101 handling below).
                var webSocket = response.StatusCode == 101 && IsWebSocketUpgrade(response.Headers)
                    ? new WebSocketLog(DateTimeOffset.Now, ContentDecoder.FindHeader(response.Headers, "Sec-WebSocket-Extensions"))
                    : null;
                var flags = WithTruncationFlags(WithTlsFlags(extensionSession?.Flags, timings), relayedRequestBody, relayedResponseBody);
                var session = sessionStore.Add(
                    targetHost, startedAt, request, response, flags, scheme, timings, webSocket);
                ProxyEngine.Log("session",
                    $"#{session.Id} {response.StatusCode} {request.Method} {scheme}://{targetHost}{request.Target} " +
                    $"({request.Body.Length} B req, {response.Body.Length} B resp)");

                if (response.StatusCode == 101)
                {
                    // 101 Switching Protocols: no longer HTTP/1.1, so relay both directions
                    // raw for the rest of the connection, reading WebSocket messages on the
                    // way through.
                    if (webSocket is not null)
                    {
                        await WebSocketRelay.RelayAsync(
                            clientTls, upstreamStream, webSocket, WebSocketCompression.FromHeader(webSocket.Extensions), cancellationToken);
                    }
                    else
                    {
                        await RelayRawBytesUntilClosedAsync(clientTls, upstreamStream, cancellationToken);
                    }

                    return;
                }

                // A response whose body ran until the server closed the
                // connection, or either side asking to close, ends this
                // connection too: the client can't tell where a further
                // response would start.
                if ((!responseHasNoBody && Http1MessageReader.IsCloseDelimited(responsePreamble.Headers)) ||
                    AsksToClose(request.HttpVersion, request.Headers) || AsksToClose(responsePreamble.HttpVersion, responsePreamble.Headers))
                {
                    return;
                }
            }
        }
        finally
        {
            upstreamTls?.Dispose();
            upstreamClient?.Dispose();
        }
    }

    /// <summary>
    /// Records a connection AutoResponder ended with <c>*reset</c> or
    /// <c>*drop</c>, so it still shows in the list. Status 0 means no response
    /// was sent.
    /// </summary>
    private static void RecordAutoRespondedSession(
        SessionStore sessionStore, string targetHost, string scheme, DateTimeOffset startedAt, CapturedRequest request, string reason,
        SessionTimings timings)
    {
        var response = new CapturedResponse("HTTP/1.1", 0, reason, [], []);
        var session = sessionStore.Add(targetHost, startedAt, request, response, flags: null, scheme, timings);
        ProxyEngine.Log("session", $"#{session.Id} {request.Method} {scheme}://{targetHost}{request.Target} -- {reason}");
    }

    private static string DescribeRoute(UpstreamProxy? route) =>
        route is null ? "a direct connection" : $"the proxy {route}";

    /// <summary>
    /// <paramref name="flags"/> plus, when the server asked for a client
    /// certificate, <see cref="ServerTlsInfo.ClientCertificateRequestedFlag"/>
    /// (which the Notes tab shows).
    /// </summary>
    private static IReadOnlyDictionary<string, string>? WithTlsFlags(IReadOnlyDictionary<string, string>? flags, SessionTimings? timings)
    {
        if (timings?.Tls is not { ClientCertificateRequested: true } tls)
        {
            return flags;
        }

        var combined = flags is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
        combined[ServerTlsInfo.ClientCertificateRequestedFlag] = tls.DescribeAcceptableIssuers();
        return combined;
    }

    /// <summary>
    /// <paramref name="flags"/> plus <see cref="Session.RequestBodyTruncatedFlag"/>/
    /// <see cref="Session.ResponseBodyTruncatedFlag"/> for a relayed body
    /// that was only partly kept.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? WithTruncationFlags(
        IReadOnlyDictionary<string, string>? flags, Http1MessageReader.RelayedBody? requestBody, Http1MessageReader.RelayedBody? responseBody)
    {
        if (requestBody is not { Truncated: true } && responseBody is not { Truncated: true })
        {
            return flags;
        }

        var combined = flags is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(flags, StringComparer.OrdinalIgnoreCase);
        if (requestBody is { Truncated: true } request)
        {
            combined[Session.RequestBodyTruncatedFlag] = DescribeTruncation(request);
        }

        if (responseBody is { Truncated: true } response)
        {
            combined[Session.ResponseBodyTruncatedFlag] = DescribeTruncation(response);
        }

        return combined;
    }

    private static string DescribeTruncation(Http1MessageReader.RelayedBody body) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"kept the first {body.Captured.Length:N0} of {body.Length:N0} bytes");

    /// <summary>The response sent when VioLens can't reach the server, with an optional explanation.</summary>
    private static CapturedResponse BadGateway(string host, int port, Exception ex, string? explanation = null)
    {
        var body = Encoding.UTF8.GetBytes(
            $"{ProxyEngine.Identity.ProductName} couldn't connect to {host}:{port}: {ex.Message}" + (explanation is null ? string.Empty : $"\r\n\r\n{explanation}"));
        return new CapturedResponse(
            "HTTP/1.1",
            502,
            "Bad Gateway",
            [("Content-Type", "text/plain; charset=utf-8"), ("Content-Length", body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Connection", "close")],
            body);
    }

    /// <summary>
    /// True when a message ends its connection: its Connection header
    /// includes "close", or it's HTTP/1.0 without "keep-alive" (RFC 9112
    /// section 9.3).
    /// </summary>
    private static bool AsksToClose(string httpVersion, IReadOnlyList<(string Name, string Value)> headers)
    {
        var keepAlive = false;
        foreach (var (name, value) in headers)
        {
            if (!string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var token in value.Split(','))
            {
                if (string.Equals(token.Trim(), "close", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                keepAlive |= string.Equals(token.Trim(), "keep-alive", StringComparison.OrdinalIgnoreCase);
            }
        }

        return string.Equals(httpVersion, "HTTP/1.0", StringComparison.OrdinalIgnoreCase) && !keepAlive;
    }

    /// <summary>Applies AutoResponder <c>*header:</c> overrides: replace by name, or append.</summary>
    private static IReadOnlyList<(string Name, string Value)> MergeHeaders(
        IReadOnlyList<(string Name, string Value)> original, IReadOnlyList<(string Name, string Value)> overrides)
    {
        var merged = new List<(string Name, string Value)>(original);
        foreach (var (name, value) in overrides)
        {
            var index = merged.FindIndex(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                merged[index] = (name, value);
            }
            else
            {
                merged.Add((name, value));
            }
        }

        return merged;
    }

    private static Task<CapturedResponse> BuildMockResponseAsync(
        AutoResponderOutcome outcome, CapturedRequest request, HttpClient proxyUrlClient, CancellationToken cancellationToken) => outcome.FinalActionKind switch
    {
        AutoResponderActionKind.ServeFile => BuildServeFileResponseAsync(outcome.Text!, cancellationToken),
        AutoResponderActionKind.ProxyUrl => FetchAndBuildResponseAsync(proxyUrlClient, outcome.Text!, cancellationToken),
        AutoResponderActionKind.Redirect => Task.FromResult(BuildRedirectResponse(outcome.Text!)),
        AutoResponderActionKind.CorsPreflightAllow => Task.FromResult(BuildCorsPreflightResponse(request)),
        _ => throw new InvalidOperationException($"Unexpected AutoResponder final action: {outcome.FinalActionKind}."),
    };

    private static async Task<CapturedResponse> BuildServeFileResponseAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            var missingBody = Encoding.UTF8.GetBytes($"{ProxyEngine.Identity.ProductName} AutoResponder: file not found -- {path}");
            return new CapturedResponse(
                "HTTP/1.1", 404, "Not Found", [("Content-Type", "text/plain; charset=utf-8")], missingBody);
        }

        var body = await File.ReadAllBytesAsync(path, cancellationToken);
        return new CapturedResponse("HTTP/1.1", 200, "OK", [("Content-Type", GuessContentType(path))], body);
    }

    /// <summary>
    /// Common file types only; anything else is application/octet-stream. A
    /// non-final <c>*header:Content-Type=</c> rule can override it.
    /// </summary>
    private static string GuessContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".xml" => "application/xml",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };

    /// <summary>
    /// AutoResponder URL proxying: fetch another URL and serve its response
    /// as-is, without decompressing it.
    /// </summary>
    private static async Task<CapturedResponse> FetchAndBuildResponseAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var upstreamResponse = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await upstreamResponse.Content.ReadAsByteArrayAsync(cancellationToken);

            var headers = new List<(string Name, string Value)>();
            foreach (var header in upstreamResponse.Headers)
            {
                headers.AddRange(header.Value.Select(value => (header.Key, value)));
            }

            foreach (var header in upstreamResponse.Content.Headers)
            {
                headers.AddRange(header.Value.Select(value => (header.Key, value)));
            }

            return new CapturedResponse(
                "HTTP/1.1", (int)upstreamResponse.StatusCode, upstreamResponse.ReasonPhrase ?? string.Empty, headers, body);
        }
        catch (Exception ex)
        {
            var body = Encoding.UTF8.GetBytes($"{ProxyEngine.Identity.ProductName} AutoResponder: fetching '{url}' failed -- {ex.Message}");
            return new CapturedResponse("HTTP/1.1", 502, "Bad Gateway", [("Content-Type", "text/plain; charset=utf-8")], body);
        }
    }

    private static CapturedResponse BuildRedirectResponse(string url) =>
        new("HTTP/1.1", 302, "Found", [("Location", url)], []);

    /// <summary>
    /// Answers an OPTIONS preflight with CORS headers echoing what the browser
    /// asked for. Fiddler doesn't document its exact response, so this is our
    /// own reading of it.
    /// </summary>
    private static CapturedResponse BuildCorsPreflightResponse(CapturedRequest request)
    {
        var origin = FindHeaderValue(request.Headers, "Origin") ?? "*";
        var requestedMethod = FindHeaderValue(request.Headers, "Access-Control-Request-Method");
        var requestedHeaders = FindHeaderValue(request.Headers, "Access-Control-Request-Headers");

        var headers = new List<(string Name, string Value)>
        {
            ("Access-Control-Allow-Origin", origin),
            ("Access-Control-Allow-Methods", requestedMethod ?? "GET, POST, PUT, PATCH, DELETE, OPTIONS"),
            ("Access-Control-Allow-Headers", requestedHeaders ?? "*"),
            ("Access-Control-Allow-Credentials", "true"),
            ("Access-Control-Max-Age", "86400"),
        };

        return new CapturedResponse("HTTP/1.1", 204, "No Content", headers, []);
    }

    private static string? FindHeaderValue(IReadOnlyList<(string Name, string Value)> headers, string name)
    {
        foreach (var (headerName, value) in headers)
        {
            if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Whether a 101 response switched to WebSocket (Upgrade: websocket).</summary>
    private static bool IsWebSocketUpgrade(IReadOnlyList<(string Name, string Value)> headers) =>
        ContentDecoder.FindHeader(headers, "Upgrade")?.Split(',').Any(p => string.Equals(p.Trim(), "websocket", StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task RelayRawBytesUntilClosedAsync(
        Stream clientTls, Stream upstreamTls, CancellationToken cancellationToken)
    {
        var clientToUpstream = clientTls.CopyToAsync(upstreamTls, cancellationToken);
        var upstreamToClient = upstreamTls.CopyToAsync(clientTls, cancellationToken);

        try
        {
            await Task.WhenAll(clientToUpstream, upstreamToClient);
        }
        catch (Exception)
        {
            // A one-sided close is the normal way an upgraded connection
            // like a WebSocket ends here; nothing to act on.
        }
    }

    private static (string Host, int Port) ParseConnectTarget(string target)
    {
        var separatorIndex = target.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            return (target, 443);
        }

        var host = target[..separatorIndex];
        var portText = target[(separatorIndex + 1)..];
        return int.TryParse(portText, out var port) ? (host, port) : (host, 443);
    }
}
