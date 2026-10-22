using System.Collections.Concurrent;
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
/// An intercepting HTTP proxy: accepts a CONNECT tunnel and terminates TLS
/// toward the client with a freshly-signed leaf, or takes plain http://
/// proxy requests as they come (see <see cref="PlainHttpRequest"/>), and
/// reads each request/response pair on the connection as HTTP/1.1 (see
/// <see cref="Http1MessageReader"/>), capturing it into
/// <see cref="SessionStore"/> along the way. The upstream connection
/// itself is opened lazily, inside <c>PumpSessionsAsync</c>, the first time
/// a request actually needs to reach the real server -- see that method's
/// own remarks on why <see cref="AutoResponderRules"/> requires that.
///
/// Every message is read as a preamble first (start line + headers only),
/// which is enough for <see cref="BreakpointManager"/> and
/// <see cref="AutoResponderRules"/> to both decide what happens next --
/// <c>PumpSessionsAsync</c> below only buffers a body fully (so it can be
/// edited, or so a fully-local response can be built) when that's actually
/// going to happen; otherwise the body is relayed live via
/// <see cref="Http1MessageReader.RelayBodyAsync"/> instead. That split
/// matters for anything long-lived -- a streamed chat response, a large
/// download -- where buffering the whole thing first would mean the other
/// side sees nothing at all until it's completely finished.
///
/// Still deliberately narrow: one connection is handled as a strict
/// request-then-response ping-pong (no pipelining), and a handful of
/// framing edge cases aren't covered yet -- see Http1MessageReader's
/// remarks. That's enough to prove the session model and, next, the SAZ
/// writer against real traffic, which is the rest of Phase 1.
///
/// FiddlerScript's own two hook points (<c>Handlers.OnBeforeRequest</c>/
/// <c>OnBeforeResponse</c>, via an optional <see cref="IScriptRunner"/>)
/// are wired in on the same "buffer only when something will actually act on
/// it" fork described above -- see <c>PumpSessionsAsync</c>'s own remarks for
/// exactly where they run and what's deliberately still out of scope. Loaded
/// .NET extensions' own <c>IAutoTamper</c> hooks (via an optional
/// <see cref="IExtensionAutoTamperHost"/>) share that exact same fork and
/// ordering -- see that interface's own remarks for why FiddlerScript's
/// handler runs first.
/// </summary>
public sealed class InterceptingProxyListener
{
    /// <summary>
    /// For AutoResponder rules that answer with another URL's content. Goes
    /// out the way VioLens's own connections do (see
    /// <see cref="UpstreamWebProxy"/>): never through the system proxy,
    /// which while capturing is VioLens itself, so the fetch isn't
    /// captured as a session of its own.
    /// </summary>
    private readonly UpstreamWebProxy _proxyUrl;

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
        _proxyUrl = new UpstreamWebProxy(this);
        // A fresh, untouched manager/rules set has nothing configured, so
        // BreakpointRules.AnyActive / AutoResponderRules.AnyActive are both
        // false and every call this listener makes into either is a single
        // boolean check that returns immediately -- callers that don't care
        // about breakpoints or AutoResponder (tests, the console dev host)
        // get behavior identical to before either existed.
        _breakpointManager = breakpointManager ?? new BreakpointManager();
        _autoResponderRules = autoResponderRules ?? new AutoResponderRules();
        // No default instance here, unlike the two above -- a null
        // scriptRunner just means "no script loaded," and every call site
        // below already treats that the same way AutoResponderRules.AnyActive
        // being false does: HasOnBeforeRequest/HasOnBeforeResponse are read
        // through a null-conditional, so nothing needs a real no-op
        // implementation to stay safe.
        _scriptRunner = scriptRunner;
        // Same reasoning as _scriptRunner immediately above: a null
        // extensionHost just means "no .NET extensions loaded," and every
        // call site reads HasAnyRequestBeforeHandlers/HasAnyResponseBeforeHandlers
        // through a null-conditional.
        _extensionHost = extensionHost;
        // Ported Fiddler-shaped extensions (see IExtensionSessionHost). Null,
        // or IsActive false, means none are loaded.
        _sessionHost = sessionHost;
        AllowsRemoteClients = allowRemoteClients;
        _listener = new TcpListener(allowRemoteClients ? IPAddress.Any : IPAddress.Loopback, port);
    }

    /// <summary>
    /// The port actually being listened on. Only meaningful after
    /// <see cref="Start"/> -- reading it before that throws, same as
    /// <see cref="TcpListener.LocalEndpoint"/> does. Needed because
    /// <see cref="StartOnAvailablePort"/> may bind somewhere other than the
    /// port that was asked for.
    /// </summary>
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

    /// <summary>The default for <see cref="ClientIdleTimeout"/>.</summary>
    public static readonly TimeSpan DefaultClientIdleTimeout = TimeSpan.FromMinutes(2);

    private long _clientIdleTimeoutTicks = DefaultClientIdleTimeout.Ticks;

    /// <summary>
    /// How long a client connection may take to send each request's start
    /// line and headers (two minutes unless changed), or
    /// <see cref="Timeout.InfiniteTimeSpan"/> for no limit. Covers the
    /// first request, the gap between requests on a kept-alive connection,
    /// and the TLS handshake after CONNECT; a connection that runs out of
    /// time is closed quietly, the same way a client ending a keep-alive
    /// connection is. Without it, a client that connects and then says
    /// nothing (or stops halfway through its headers) holds a connection
    /// and its task for ever -- a nuisance on loopback, and a way to tie
    /// the listener up once <see cref="AllowsRemoteClients"/> opens it to
    /// the network.
    ///
    /// Deliberately only the preamble: once a request has started, nothing
    /// here times it out. A request or response body streamed slowly (a
    /// chat reply, a long upload), a session paused at a breakpoint while
    /// someone edits it, and a WebSocket or other upgraded connection after
    /// its 101 can all legitimately sit quiet for far longer than this.
    /// Read when each client connection opens, like
    /// <see cref="MaxCapturedBodyBytes"/>.
    /// </summary>
    public TimeSpan ClientIdleTimeout
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _clientIdleTimeoutTicks));
        set
        {
            if (value != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            }

            Interlocked.Exchange(ref _clientIdleTimeoutTicks, value.Ticks);
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
    /// Starts on <paramref name="preferredPort"/> when it's free. When
    /// something else already holds it -- in practice, almost always
    /// another VioLens process still running (the console dev host, or a
    /// previous launch of this app that didn't shut down cleanly) -- falls
    /// back to whatever free port the OS hands out, rather than failing
    /// outright. Callers read the actual port back from <see cref="Port"/>.
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
            // Port 0 tells the OS to hand back any free port -- the same
            // trick ASP.NET Core's test host and countless other tools use
            // to avoid ever hard-failing on a port collision.
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
    /// signed-in account. <see cref="Client"/> is the <see cref="HttpClient"/>
    /// that goes out this way.
    ///
    /// <see cref="IWebProxy"/> is synchronous, and HttpClient asks it for
    /// every request (twice: <see cref="IsBypassed"/>, then
    /// <see cref="GetProxy"/>), while a PAC script's answer is
    /// asynchronous -- the script may still be downloading, and running it
    /// happens off-thread (see <see cref="ProxyAutoConfig.GetRoutesAsync"/>).
    /// So a PAC answer is kept here per scheme, host and port for
    /// <see cref="RouteLifetime"/>, and <see cref="PrepareRouteAsync"/> fills
    /// that in asynchronously ahead of a request, leaving the synchronous
    /// question nothing to wait for. Only a question that arrives with
    /// nothing prepared (HttpClient following a redirect to another host,
    /// say) still blocks, once per host. Keyed by host rather than the whole
    /// URL, so a script that routes plain-HTTP requests by path gets its
    /// answer for the first path asked about -- the same per-host shortcut
    /// browsers' own PAC caches take, and the only way the cache helps at all
    /// for a host whose paths are all different. A gateway, or no proxy
    /// configuration at all, is decided without waiting, so isn't cached.
    /// </summary>
    internal sealed class UpstreamWebProxy : IWebProxy
    {
        /// <summary>How long a PAC script's answer for one host is reused.</summary>
        public static readonly TimeSpan RouteLifetime = TimeSpan.FromSeconds(30);

        private const int MaxCachedRoutes = 256;

        private readonly InterceptingProxyListener _listener;
        private readonly ConcurrentDictionary<string, CachedRoute> _routes = new(StringComparer.OrdinalIgnoreCase);

        public UpstreamWebProxy(InterceptingProxyListener listener)
        {
            _listener = listener;
            Client = new HttpClient(new SocketsHttpHandler
            {
                Proxy = this,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.None,
            });
        }

        public HttpClient Client { get; }

        public ICredentials? Credentials
        {
            get => (ICredentials?)_listener.ProxyCredential ?? CredentialCache.DefaultCredentials;
            set { }
        }

        public Uri? GetProxy(Uri destination) =>
            Route(destination) is { } proxy ? new Uri($"http://{proxy}/") : destination;

        public bool IsBypassed(Uri host) => Route(host) is null;

        /// <summary>
        /// Works out the route for <paramref name="destination"/> without
        /// blocking, so the synchronous <see cref="GetProxy"/>/
        /// <see cref="IsBypassed"/> that follow find it ready.
        /// </summary>
        public async Task PrepareRouteAsync(Uri destination, CancellationToken cancellationToken)
        {
            if (UsesAutoConfig(destination) is not { } autoConfig || TryGetCached(destination, autoConfig, out _))
            {
                return;
            }

            var routes = await ChooseAsync(destination, cancellationToken);
            Remember(destination, autoConfig, routes);
        }

        private UpstreamProxy? Route(Uri destination)
        {
            if (UsesAutoConfig(destination) is not { } autoConfig)
            {
                // Loopback, a gateway, or no configuration: ChooseRoutesAsync
                // decides those before its first await, so this doesn't block.
                return First(ChooseAsync(destination, CancellationToken.None).GetAwaiter().GetResult());
            }

            if (TryGetCached(destination, autoConfig, out var cached))
            {
                return cached;
            }

            // Nothing prepared: wait, once for this host. ProxyAutoConfig's
            // own answer cache often has it anyway, and then this task has
            // already finished.
            var routes = ChooseAsync(destination, CancellationToken.None).GetAwaiter().GetResult();
            return Remember(destination, autoConfig, routes);
        }

        /// <summary>The PAC script <paramref name="destination"/> would be routed by, or null when none is consulted.</summary>
        private ProxyAutoConfig? UsesAutoConfig(Uri destination) =>
            _listener.Gateway is null && _listener.AutoConfig is { } autoConfig && !UpstreamProxy.IsLoopback(destination.IdnHost)
                ? autoConfig
                : null;

        private Task<IReadOnlyList<UpstreamProxy?>> ChooseAsync(Uri destination, CancellationToken cancellationToken)
        {
            var isTls = string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            return _listener.ChooseRoutesAsync(isTls, destination.IdnHost, destination.Port, destination.PathAndQuery, cancellationToken);
        }

        private bool TryGetCached(Uri destination, ProxyAutoConfig autoConfig, out UpstreamProxy? route)
        {
            // An entry from a script that's since been replaced doesn't count.
            if (_routes.TryGetValue(Key(destination), out var cached) &&
                ReferenceEquals(cached.AutoConfig, autoConfig) && Environment.TickCount64 < cached.ExpiresAt)
            {
                route = cached.Route;
                return true;
            }

            route = null;
            return false;
        }

        private UpstreamProxy? Remember(Uri destination, ProxyAutoConfig autoConfig, IReadOnlyList<UpstreamProxy?> routes)
        {
            if (_routes.Count >= MaxCachedRoutes)
            {
                _routes.Clear();
            }

            var route = First(routes);
            _routes[Key(destination)] = new CachedRoute(autoConfig, route, Environment.TickCount64 + (long)RouteLifetime.TotalMilliseconds);
            return route;
        }

        private static string Key(Uri destination) => $"{destination.Scheme}://{destination.IdnHost}:{destination.Port}";

        private static UpstreamProxy? First(IReadOnlyList<UpstreamProxy?> routes) => routes.Count > 0 ? routes[0] : null;

        private sealed record CachedRoute(ProxyAutoConfig AutoConfig, UpstreamProxy? Route, long ExpiresAt);
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
            var idleTimeout = ClientIdleTimeout;
            var first = await ReadRequestPreambleWithinAsync(clientStream, idleTimeout, cancellationToken);
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
                    _breakpointManager, _autoResponderRules, _scriptRunner, _extensionHost, _sessionHost, ChooseRoutesAsync, () => ProxyCredential, _proxyUrl, Port,
                    () => _leafProvider.IssuerCertificateDer, MaxCapturedBodyBytes, idleTimeout, cancellationToken);
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
                // Let the OS pick the best mutually-supported protocol
                // (TLS 1.2/1.3) rather than pinning one here.
                EnabledSslProtocols = SslProtocols.None,
            };
            // The handshake falls under the idle timeout too: a client that
            // sends CONNECT and then nothing else would otherwise hold the
            // connection just as surely as one that never sends a request.
            using (var handshakeTimer = StartIdleTimer(idleTimeout, cancellationToken))
            {
                try
                {
                    await clientTls.AuthenticateAsServerAsync(serverOptions, handshakeTimer.Token);
                }
                catch (Exception) when (handshakeTimer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    ProxyEngine.Log("proxy", $"{targetHost}: the client didn't finish its TLS handshake in time; closing the connection.");
                    return;
                }
            }

            // The upstream connection is NOT made here anymore -- see
            // PumpSessionsAsync's own remarks on why it has to be lazy.
            await PumpSessionsAsync(
                isTls: true, targetHost, targetPort, client, new ReadBufferedStream(clientTls), firstRequest: null, _sessionStore, _breakpointManager,
                _autoResponderRules, _scriptRunner, _extensionHost, _sessionHost, ChooseRoutesAsync, () => ProxyCredential, _proxyUrl, Port,
                () => _leafProvider.IssuerCertificateDer, MaxCapturedBodyBytes, idleTimeout, cancellationToken);
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
        // AuthenticationException's own Message is almost always the
        // unhelpful "Authentication failed, see inner exception." -- the
        // actual reason lives in InnerException (sometimes nested a
        // couple of levels deep), so walk the chain instead of printing
        // just the outer wrapper.
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
    /// One CONNECT tunnel's worth of request/response pairs. The upstream
    /// TLS connection is opened lazily, the first time a request is
    /// actually going to reach the real server, and reused for every later
    /// request on this same tunnel -- never opened at all for a tunnel
    /// where <see cref="AutoResponderRules"/> answers every single request
    /// itself. That laziness is the whole point of AutoResponder's offline
    /// testing use case (see the Project Plan doc's Requirements-derived
    /// notes): a rule that serves a local file or returns <c>*drop</c>
    /// needs to work with no route to the real server at all, which an
    /// eager upstream connect (the design before AutoResponder existed)
    /// would have broken outright.
    ///
    /// <paramref name="scriptRunner"/>'s two hook points run only on the
    /// "forward to the real server" path below, never for a request
    /// AutoResponder answers locally (that local-answer branch returns
    /// before this parameter is ever consulted) -- a deliberate scope cut,
    /// not confirmed to match real Fiddler's own ordering; see the
    /// FiddlerScript Compatibility Design doc. On that forward path, each
    /// hook forces the same body-buffering a matching breakpoint or
    /// AutoResponder force-flag already forces (see
    /// <see cref="IScriptRunner.HasOnBeforeRequest"/>/
    /// <see cref="IScriptRunner.HasOnBeforeResponse"/>), and runs
    /// *before* the corresponding breakpoint check -- the same order real
    /// Fiddler uses (a script can set <c>oSession["x-breakrequest"]</c> to
    /// arm a pause from inside <c>OnBeforeRequest</c> itself), even though
    /// nothing here actually reads that flag back yet -- see
    /// <c>BreakpointManager</c>'s own remarks on the pre-existing gap this
    /// mirrors rather than silently fixes.
    ///
    /// <paramref name="extensionHost"/>'s Before hooks join that exact same
    /// fork and run immediately after <paramref name="scriptRunner"/>'s own
    /// (an assumed, not confirmed-against-real-Fiddler ordering -- see
    /// <see cref="IExtensionAutoTamperHost"/>'s own remarks), so a loaded
    /// extension always sees whatever FiddlerScript already edited. The
    /// After hooks (<see cref="IExtensionAutoTamperHost.RunRequestAfter"/>/
    /// <see cref="IExtensionAutoTamperHost.RunResponseAfter"/>) run
    /// unconditionally right after the corresponding write completes, on
    /// both the buffered and relay-live paths -- they never need to force
    /// buffering (see that interface's own remarks on why), so unlike the
    /// Before hooks they're called from a single spot after each fork
    /// rejoins rather than from inside either branch.
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
        UpstreamWebProxy proxyUrl,
        int ownPort,
        Func<byte[]> rootCertificateDer,
        long maxCapturedBodyBytes,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken)
    {
        // clientTls is the client's TLS stream for a CONNECT tunnel, or its
        // plain connection for http:// requests (isTls false). A plain
        // connection can carry requests for different servers one after
        // another, so for those targetHost/targetPort are set per request
        // and the server connection is replaced when they change.
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

        // Opens the upstream connection the first time a request on this
        // tunnel actually has to reach the real server, then reuses it. A
        // request that AutoResponder or an extension answers never gets
        // here, so a host that doesn't even resolve (the CSP extension's
        // report host, say) still works.
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
                // Directly, through the network's proxy, or as its PAC
                // script says -- trying the next route when one can't be
                // reached, as browsers do.
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

                    // The probe notices a server asking for a client
                    // certificate (mutual TLS): that request comes to
                    // VioLens, not the browser, so it's recorded on the
                    // session instead. Default certificate validation,
                    // deliberately: this proxy should surface a real
                    // upstream cert problem, not hide it, even though it's
                    // standing in the middle of the connection for the
                    // client's side.
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

        // Opens the server connection, or, if that fails (the name doesn't
        // resolve, nothing's listening, the network's proxy refuses), answers
        // the client with 502 Bad Gateway and records that as the session --
        // returns null then, and the connection ends.
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
                var requestPreamble = pendingRequest ?? await ReadRequestPreambleWithinAsync(clientTls, idleTimeout, cancellationToken);
                pendingRequest = null;
                if (requestPreamble is null)
                {
                    // The client closed the connection between requests --
                    // the normal way a keep-alive HTTP/1.1 connection ends --
                    // or left it idle past ClientIdleTimeout, which ends it
                    // the same way.
                    return;
                }

                // Timed from the request's arrival, not from when the
                // connection started waiting for it (keep-alive idle time).
                var startedAt = DateTimeOffset.Now;
                var timing = new SessionTimingRecorder();
                currentTiming = timing;

                if (!isTls)
                {
                    // Store and process plain HTTP the way HTTPS is: a
                    // path-only target plus a Host header (see
                    // PlainHttpRequest). A later request on the same
                    // connection may be for VioLens itself, or something
                    // that can't be forwarded.
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

                // AutoResponderRules only ever looks at method/URL, never
                // the body -- same reasoning as BreakpointRules.ShouldBreakBeforeRequest
                // (see its own remarks) -- so this, too, can be decided
                // from the preamble alone.
                var url = isTls
                    ? $"https://{targetHost}{requestPreamble.Target}"
                    : $"http://{ContentDecoder.FindHeader(requestPreamble.Headers, "Host") ?? targetHost}{requestPreamble.Target}";
                var outcome = autoResponderRules.AnyActive
                    ? autoResponderRules.Evaluate(requestPreamble.Method, url)
                    : AutoResponderOutcome.PassThrough;

                if (outcome.DelayMilliseconds > 0)
                {
                    // Applies whether the request ends up answered locally
                    // or forwarded for real -- Fiddler's own *delay: is a
                    // general "make this exchange slower," not tied to
                    // either path specifically.
                    await Task.Delay(outcome.DelayMilliseconds, cancellationToken);
                }

                var isAnsweredLocally = outcome.FinalActionKind is
                    AutoResponderActionKind.ServeFile or AutoResponderActionKind.ProxyUrl or AutoResponderActionKind.Redirect or
                    AutoResponderActionKind.Reset or AutoResponderActionKind.Drop or AutoResponderActionKind.CorsPreflightAllow;

                if (isAnsweredLocally)
                {
                    // Always buffer the full body here rather than relaying
                    // it -- nothing downstream of this branch forwards
                    // anything live, so RelayBodyAsync's whole reason to
                    // exist (see Http1MessageReader's remarks) doesn't
                    // apply.
                    var localRequestBody = await Http1MessageReader.ReadBodyAsync(clientTls, requestPreamble.Headers, cancellationToken);
                    var localRequest = requestPreamble with { Body = localRequestBody };

                    if (outcome.FinalActionKind == AutoResponderActionKind.Reset)
                    {
                        RecordAutoRespondedSession(sessionStore, targetHost, scheme, startedAt, localRequest, "Connection Reset (AutoResponder *reset)", timing.Build(answeredLocally: true));
                        // A graceful Dispose sends a normal FIN; a TCP RST
                        // needs the zero-second linger option set first --
                        // this is the one case that needs the raw
                        // TcpClient/Socket rather than just the streams
                        // wrapping it, which is why it's passed in here at
                        // all.
                        clientConnection.Client.LingerState = new LingerOption(true, 0);
                        clientConnection.Close();
                        return;
                    }

                    if (outcome.FinalActionKind == AutoResponderActionKind.Drop)
                    {
                        RecordAutoRespondedSession(sessionStore, targetHost, scheme, startedAt, localRequest, "Connection Dropped (AutoResponder *drop)", timing.Build(answeredLocally: true));
                        // No response, no RST -- just stop. The outer
                        // `using` declarations in HandleClientAsync close
                        // this connection normally once this method returns.
                        return;
                    }

                    var localResponse = await BuildMockResponseAsync(outcome, localRequest, proxyUrl, cancellationToken);
                    await HttpMessageWriter.WriteResponseAsync(clientTls, localResponse, cancellationToken);
                    timing.Done();
                    var localSession = sessionStore.Add(targetHost, startedAt, localRequest, localResponse, flags: null, scheme, timing.Build(answeredLocally: true));
                    ProxyEngine.Log("session",
                        $"#{localSession.Id} {localResponse.StatusCode} {localRequest.Method} {url} " +
                        $"(AutoResponder: {outcome.FinalActionKind})");
                    continue;
                }

                // Not answered by AutoResponder. The upstream connection is
                // opened later, by EnsureUpstreamAsync, once it's clear a
                // ported extension isn't answering this request itself.
                var outgoingPreamble = outcome.HeadersToSet.Count > 0
                    ? requestPreamble with { Headers = MergeHeaders(requestPreamble.Headers, outcome.HeadersToSet) }
                    : requestPreamble;

                // Ported Fiddler-shaped extensions get one session object for
                // this whole request (see IExtensionSessionHost). Their
                // hooks run right after the VioLens-native extensions'
                // matching ones, an assumed ordering like the one described
                // in this method's remarks.
                var extensionSession = sessionHost is { IsActive: true }
                    ? sessionHost.BeginSession(sessionStore.PeekNextId(), targetHost, scheme)
                    : null;
                extensionSession?.PeekAtRequestHeaders(outgoingPreamble);

                // BreakpointRules.ShouldBreakBeforeRequest only ever looks
                // at Method/Target, never the body, so this can be decided
                // from the preamble alone -- before a single byte of the
                // request body has been read. That's what makes the fork
                // below possible: buffer the body only when something is
                // actually going to pause on it, relay it live otherwise.
                // outcome.ForceBreakpointBeforeRequest folds AutoResponder's
                // own *bpu into the exact same fork, rather than needing a
                // second, parallel pause mechanism -- scriptRunner's own
                // HasOnBeforeRequest joins that same fork for exactly the
                // same reason: a loaded script that defines the handler is
                // always going to want the body, same as a breakpoint would.
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

                    // FiddlerScript's OnBeforeRequest, when the loaded
                    // script defines it, runs before the breakpoint check
                    // just below -- see this method's own remarks on why
                    // that ordering (and not the reverse) is what matches
                    // real Fiddler.
                    if (mustBufferRequestForScript)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        request = scriptRunner!.RunOnBeforeRequest(peekedSessionId, targetHost, request).Request;
                    }

                    // Loaded .NET extensions' own AutoTamperRequestBefore
                    // hooks run next, after FiddlerScript's own handler --
                    // see this method's own remarks on that ordering.
                    if (mustBufferRequestForExtensions)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        request = extensionHost!.RunRequestBefore(peekedSessionId, targetHost, request);
                    }

                    // Ported extensions' AutoTamperRequestBefore. One of
                    // them may answer the request itself, in which case it
                    // never reaches the server (or a request breakpoint).
                    if (extensionSession is not null)
                    {
                        var result = extensionSession.RequestBefore(request);
                        request = result.Request;
                        extensionLocalResponse = result.LocalResponse;
                    }

                    if (extensionLocalResponse is null)
                    {
                        // A breakpoint here (Fiddler's "bpu"/bpm/break-on-all-requests,
                        // or AutoResponder's own *bpu) can hold this connection
                        // open and edit the request before it's forwarded.
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
                    // An extension answered (Fiddler's
                    // utilCreateResponseAndBypassServer). Nothing was sent,
                    // so the request-after hooks don't run. The response
                    // hooks do, as they would for any response (an assumed
                    // match with Fiddler Classic, not a confirmed one).
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

                // Fire-and-observe -- see this method's own remarks on why
                // this runs unconditionally, from one spot after both
                // branches above, rather than gated and duplicated inside
                // each one.
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

                    // Same ordering rationale as the request side: the
                    // script's own OnBeforeResponse runs before a human
                    // ever sees this exchange at a breakpoint.
                    if (mustBufferResponseForScript)
                    {
                        var peekedSessionId = sessionStore.PeekNextId();
                        response = scriptRunner!.RunOnBeforeResponse(peekedSessionId, targetHost, request, response).Response;
                    }

                    // Loaded .NET extensions' own AutoTamperResponseBefore
                    // hooks run next, after FiddlerScript's own handler --
                    // same ordering as the request side above.
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
                    // The case that matters most in practice: this is what
                    // lets a long-lived streamed response -- a chat reply
                    // arriving token by token, easily tens of seconds --
                    // reach the client as it's generated instead of only
                    // once the whole thing is done.
                    await HttpMessageWriter.WriteResponsePreambleAsync(clientTls, responsePreamble, cancellationToken);
                    var responseBody = responseHasNoBody
                        ? Http1MessageReader.RelayedBody.Empty
                        : await Http1MessageReader.RelayResponseBodyAsync(
                            upstreamStream, clientTls, responsePreamble.Headers, cancellationToken, maxCapturedBodyBytes);
                    response = responsePreamble with { Body = responseBody.Captured };
                    relayedResponseBody = responseBody;
                }

                // Fire-and-observe -- see this method's own remarks on why
                // this runs unconditionally, from one spot after both
                // branches above.
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
                    // 101 Switching Protocols -- the connection just stopped
                    // being HTTP/1.1 (WebSocket is by far the common case here).
                    // Trying to read another request off it would mean parsing
                    // binary frame data as if it were an HTTP start line, which
                    // fails outright, and even before it fails it can't work
                    // right: a request-then-response loop is fundamentally
                    // half-duplex, while what's needed from here on is a full
                    // duplex, unparsed relay in both directions at once. Fall
                    // back to exactly that for the rest of this connection's
                    // life -- the same raw pump this spike used everywhere
                    // before it understood HTTP/1.1 at all. A WebSocket's
                    // messages are read out of the bytes on the way through.
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
    /// Adds a session for a connection AutoResponder ended without any real
    /// HTTP response (<c>*reset</c>/<c>*drop</c>) -- so it's still visible
    /// in the session list instead of just silently vanishing, which would
    /// read as a bug rather than the deliberate simulation it is. Status
    /// code 0 is not a real HTTP status; it's this codebase's convention
    /// for "no response was ever sent," distinguishing it in the session
    /// grid from every genuine (even error) status code.
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

    /// <summary>
    /// Overlays <paramref name="overrides"/> onto <paramref name="original"/>
    /// by header name (case-insensitive): an existing header is replaced in
    /// place, a new one is appended. Used for AutoResponder's <c>*header:</c>
    /// action -- only reached on the "forward to the real server" path (see
    /// <c>PumpSessionsAsync</c>'s own remarks on why a header override is
    /// moot for a locally-answered request).
    /// </summary>
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
        AutoResponderOutcome outcome, CapturedRequest request, UpstreamWebProxy proxyUrl, CancellationToken cancellationToken) => outcome.FinalActionKind switch
    {
        AutoResponderActionKind.ServeFile => BuildServeFileResponseAsync(outcome.Text!, cancellationToken),
        AutoResponderActionKind.ProxyUrl => FetchAndBuildResponseAsync(proxyUrl, outcome.Text!, cancellationToken),
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
    /// A small, deliberately incomplete extension-to-Content-Type map --
    /// enough for the file kinds an AutoResponder rule is actually likely
    /// to serve (a JSON/HTML/text fixture, an image). Falls back to
    /// <c>application/octet-stream</c> rather than guessing further; a
    /// person who needs a specific Content-Type for something unusual can
    /// still get it with <c>*header:Content-Type=...</c> on an earlier,
    /// non-final rule.
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
    /// Fiddler's AutoResponder "URL proxying": fetch a different URL for
    /// real and serve back whatever it returns, headers (Content-Encoding
    /// included, deliberately not auto-decompressed here -- what's served
    /// back should be exactly what that other server sent) and all.
    /// </summary>
    private static async Task<CapturedResponse> FetchAndBuildResponseAsync(UpstreamWebProxy proxyUrl, string url, CancellationToken cancellationToken)
    {
        try
        {
            // Asks for the route first, asynchronously, so HttpClient's own
            // (synchronous) question a moment later is answered from the
            // cache rather than by blocking on the PAC script.
            await proxyUrl.PrepareRouteAsync(new Uri(url), cancellationToken);
            using var upstreamResponse = await proxyUrl.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
    /// Answers an OPTIONS preflight with permissive CORS headers, mirroring
    /// whatever the browser actually asked for (<c>Origin</c>,
    /// <c>Access-Control-Request-Method</c>, <c>Access-Control-Request-Headers</c>)
    /// rather than a fixed set. Fiddler Classic's own documentation names
    /// <c>*CORSPreflightAllow</c> but doesn't spell out the exact response
    /// shape it sends -- this is a reasonable, standards-conformant
    /// interpretation, not a verified match. Flagging that here rather than
    /// presenting it as confirmed Fiddler behavior; if a real Fiddler
    /// response is ever seen to differ, this is the one place to fix.
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

    /// <summary>
    /// Passes an upgraded (101, not WebSocket) connection's bytes through in
    /// both directions until it's done. Once one side closes, the other gets
    /// <see cref="WebSocketRelay.CloseGrace"/> to finish too -- the same
    /// rule <see cref="WebSocketRelay"/> uses -- and is then cut off: just
    /// waiting for both directions would leave a connection open for ever
    /// whenever one side goes away and the other never notices (a server
    /// that closes while the client sits waiting to send, say), holding
    /// both connections and this task with it.
    /// </summary>
    private static async Task RelayRawBytesUntilClosedAsync(
        Stream clientTls, Stream upstreamTls, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clientToUpstream = clientTls.CopyToAsync(upstreamTls, stop.Token);
        var upstreamToClient = upstreamTls.CopyToAsync(clientTls, stop.Token);

        try
        {
            var first = await Task.WhenAny(clientToUpstream, upstreamToClient);
            var other = first == clientToUpstream ? upstreamToClient : clientToUpstream;
            if (await Task.WhenAny(other, Task.Delay(WebSocketRelay.CloseGrace, cancellationToken)) != other)
            {
                await stop.CancelAsync();
            }

            await Task.WhenAll(clientToUpstream, upstreamToClient);
        }
        catch (Exception)
        {
            // A reset or a one-sided close is the normal way an upgraded
            // connection ends here; nothing to act on.
        }
    }

    /// <summary>
    /// A token for one wait under <see cref="ClientIdleTimeout"/>: cancelled
    /// when <paramref name="cancellationToken"/> is, or once
    /// <paramref name="idleTimeout"/> has passed (never, for
    /// <see cref="Timeout.InfiniteTimeSpan"/>).
    /// </summary>
    private static CancellationTokenSource StartIdleTimer(TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(idleTimeout);
        return timer;
    }

    /// <summary>
    /// <see cref="Http1MessageReader.ReadRequestPreambleAsync"/>, given
    /// <paramref name="idleTimeout"/> to arrive in full. Null, as for a
    /// client that closed, when it doesn't: a client that went quiet before
    /// starting a request and one that stalled halfway through its headers
    /// both just lose the connection (see <see cref="ClientIdleTimeout"/>).
    /// </summary>
    private static async Task<CapturedRequest?> ReadRequestPreambleWithinAsync(
        Stream client, TimeSpan idleTimeout, CancellationToken cancellationToken)
    {
        using var timer = StartIdleTimer(idleTimeout, cancellationToken);
        try
        {
            return await Http1MessageReader.ReadRequestPreambleAsync(client, timer.Token);
        }
        catch (Exception) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A cancelled socket read surfaces as OperationCanceledException,
            // or as an IOException from a stream wrapped around it.
            return null;
        }
    }

    /// <summary>
    /// The host and port a CONNECT request names: <c>host:port</c>, with
    /// 443 when the port is missing or unreadable. An IPv6 address comes in
    /// brackets (<c>[::1]:443</c>, the authority form RFC 9110 requires),
    /// which are removed here -- from now on the host is used as a bare
    /// address (DNS, the leaf certificate's IP SAN, TLS), and whatever writes
    /// it back into a URL or authority (<see cref="UpstreamProxy"/>'s CONNECT,
    /// a PAC script's URL) adds them again. An IPv6 address without brackets
    /// has no room for a port -- every colon belongs to the address -- so
    /// it's taken whole, on 443.
    /// </summary>
    internal static (string Host, int Port) ParseConnectTarget(string target)
    {
        string host;
        string? portText;
        if (target.StartsWith('[') && target.IndexOf(']') is var close and > 0)
        {
            host = target[1..close];
            var rest = target[(close + 1)..];
            portText = rest.StartsWith(':') ? rest[1..] : null;
        }
        else if (target.IndexOf(':') is var separator and >= 0 && separator == target.LastIndexOf(':'))
        {
            host = target[..separator];
            portText = target[(separator + 1)..];
        }
        else
        {
            // No colon at all, or more than one: a bare IPv6 address.
            host = target;
            portText = null;
        }

        return int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) &&
               port is > 0 and <= IPEndPoint.MaxPort
            ? (host, port)
            : (host, 443);
    }
}
