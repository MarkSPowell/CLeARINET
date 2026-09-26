using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Clearinet.ProxyCore.AutoResponder;
using Clearinet.ProxyCore.Breakpoints;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Scripting;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>
/// An HTTPS-intercepting proxy: accepts a CONNECT tunnel, terminates TLS
/// toward the client with a freshly-signed leaf, and reads each
/// request/response pair on the connection as HTTP/1.1 (see
/// <see cref="Http1MessageReader"/>), capturing it into
/// <see cref="SessionStore"/> along the way. The upstream TLS connection
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
/// <c>OnBeforeResponse</c>, via an optional <see cref="IFiddlerScriptRunner"/>)
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
    private static readonly HttpClient ProxyUrlHttpClient = new();

    private readonly LeafCertificateProvider _leafProvider;
    private readonly SessionStore _sessionStore;
    private readonly BreakpointManager _breakpointManager;
    private readonly AutoResponderRules _autoResponderRules;
    private readonly IFiddlerScriptRunner? _scriptRunner;
    private readonly IExtensionAutoTamperHost? _extensionHost;
    private readonly IExtensionSessionHost? _sessionHost;
    private readonly TcpListener _listener;
    private CancellationTokenSource? _cts;

    public InterceptingProxyListener(
        int port,
        LeafCertificateProvider leafProvider,
        SessionStore sessionStore,
        BreakpointManager? breakpointManager = null,
        AutoResponderRules? autoResponderRules = null,
        IFiddlerScriptRunner? scriptRunner = null,
        IExtensionAutoTamperHost? extensionHost = null,
        IExtensionSessionHost? sessionHost = null)
    {
        _leafProvider = leafProvider;
        _sessionStore = sessionStore;
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
        _listener = new TcpListener(IPAddress.Loopback, port);
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
    /// Starts on <paramref name="preferredPort"/> when it's free. When
    /// something else already holds it -- in practice, almost always
    /// another CLeARINET process still running (the console dev host, or a
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
        IFiddlerScriptRunner? scriptRunner = null,
        IExtensionAutoTamperHost? extensionHost = null,
        IExtensionSessionHost? sessionHost = null)
    {
        var preferred = new InterceptingProxyListener(preferredPort, leafProvider, sessionStore, breakpointManager, autoResponderRules, scriptRunner, extensionHost, sessionHost);
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
            var fallback = new InterceptingProxyListener(0, leafProvider, sessionStore, breakpointManager, autoResponderRules, scriptRunner, extensionHost, sessionHost);
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
            using var clientStream = client.GetStream();
            var (method, target) = await ReadRequestLineAndHeadersAsync(clientStream, cancellationToken);

            if (method is null || !string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"[proxy] Ignoring non-CONNECT request ({method ?? "?"} {target}); this spike only handles HTTPS tunnels.");
                return;
            }

            var (targetHost, targetPort) = ParseConnectTarget(target!);

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
            await clientTls.AuthenticateAsServerAsync(serverOptions, cancellationToken);

            // The upstream connection is NOT made here anymore -- see
            // PumpSessionsAsync's own remarks on why it has to be lazy.
            await PumpSessionsAsync(
                targetHost, targetPort, client, clientTls, _sessionStore, _breakpointManager, _autoResponderRules,
                _scriptRunner, _extensionHost, _sessionHost, cancellationToken);
        }
        catch (BreakpointAbortedException ex)
        {
            Console.WriteLine($"[proxy] {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[proxy] Connection ended: {DescribeException(ex)}");
        }
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
    /// <see cref="IFiddlerScriptRunner.HasOnBeforeRequest"/>/
    /// <see cref="IFiddlerScriptRunner.HasOnBeforeResponse"/>), and runs
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
        string targetHost,
        int targetPort,
        TcpClient clientConnection,
        SslStream clientTls,
        SessionStore sessionStore,
        BreakpointManager breakpointManager,
        AutoResponderRules autoResponderRules,
        IFiddlerScriptRunner? scriptRunner,
        IExtensionAutoTamperHost? extensionHost,
        IExtensionSessionHost? sessionHost,
        CancellationToken cancellationToken)
    {
        TcpClient? upstreamClient = null;
        SslStream? upstreamTls = null;

        // Opens the upstream connection the first time a request on this
        // tunnel actually has to reach the real server, then reuses it. A
        // request that AutoResponder or an extension answers never gets
        // here, so a host that doesn't even resolve (the CSP extension's
        // report host, say) still works.
        async Task<SslStream> EnsureUpstreamAsync()
        {
            if (upstreamTls is null)
            {
                upstreamClient = new TcpClient();
                await upstreamClient.ConnectAsync(targetHost, targetPort, cancellationToken);
                upstreamTls = new SslStream(upstreamClient.GetStream(), leaveInnerStreamOpen: false);
                // Deliberately using default certificate validation
                // here: this proxy should surface a real upstream cert
                // problem, not hide it, even though it's standing in
                // the middle of the connection for the client's side.
                await upstreamTls.AuthenticateAsClientAsync(targetHost);
            }

            return upstreamTls;
        }

        try
        {
            while (true)
            {
                var startedAt = DateTimeOffset.Now;
                var requestPreamble = await Http1MessageReader.ReadRequestPreambleAsync(clientTls, cancellationToken);
                if (requestPreamble is null)
                {
                    // The client closed the connection between requests --
                    // the normal way a keep-alive HTTP/1.1 connection ends.
                    return;
                }

                // AutoResponderRules only ever looks at method/URL, never
                // the body -- same reasoning as BreakpointRules.ShouldBreakBeforeRequest
                // (see its own remarks) -- so this, too, can be decided
                // from the preamble alone.
                var url = $"https://{targetHost}{requestPreamble.Target}";
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
                        RecordAutoRespondedSession(sessionStore, targetHost, startedAt, localRequest, "Connection Reset (AutoResponder *reset)");
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
                        RecordAutoRespondedSession(sessionStore, targetHost, startedAt, localRequest, "Connection Dropped (AutoResponder *drop)");
                        // No response, no RST -- just stop. The outer
                        // `using` declarations in HandleClientAsync close
                        // this connection normally once this method returns.
                        return;
                    }

                    var localResponse = await BuildMockResponseAsync(outcome, localRequest, cancellationToken);
                    await HttpMessageWriter.WriteResponseAsync(clientTls, localResponse, cancellationToken);
                    var localSession = sessionStore.Add(targetHost, startedAt, localRequest, localResponse);
                    Console.WriteLine(
                        $"[{startedAt:HH:mm:ss}] #{localSession.Id} {localResponse.StatusCode} {localRequest.Method} {url} " +
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
                // hooks run right after the CLeARINET-native extensions'
                // matching ones, an assumed ordering like the one described
                // in this method's remarks.
                var extensionSession = sessionHost is { IsActive: true }
                    ? sessionHost.BeginSession(sessionStore.PeekNextId(), targetHost, "https")
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
                        request = await breakpointManager.ApplyRequestBreakpointAsync(targetHost, request, cancellationToken);
                        await HttpMessageWriter.WriteRequestAsync(await EnsureUpstreamAsync(), request, cancellationToken);
                    }
                }
                else
                {
                    var upstream = await EnsureUpstreamAsync();
                    await HttpMessageWriter.WriteRequestPreambleAsync(upstream, outgoingPreamble, cancellationToken);
                    var requestBody = await Http1MessageReader.RelayBodyAsync(
                        clientTls, upstream, outgoingPreamble.Headers, cancellationToken);
                    request = outgoingPreamble with { Body = requestBody };
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

                    var answeredSession = sessionStore.Add(targetHost, startedAt, request, localResponse, extensionSession.Flags);
                    Console.WriteLine(
                        $"[{startedAt:HH:mm:ss}] #{answeredSession.Id} {localResponse.StatusCode} {request.Method} {url} (answered by an extension)");
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
                if (responsePreamble is null)
                {
                    Console.WriteLine(
                        $"[proxy] {targetHost}: upstream closed the connection before answering {request.Method} {request.Target}.");
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
                        : await Http1MessageReader.ReadBodyAsync(upstreamStream, responsePreamble.Headers, cancellationToken);
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

                    response = await breakpointManager.ApplyResponseBreakpointAsync(
                        targetHost, request, response, cancellationToken);
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
                        ? Array.Empty<byte>()
                        : await Http1MessageReader.RelayBodyAsync(
                            upstreamStream, clientTls, responsePreamble.Headers, cancellationToken);
                    response = responsePreamble with { Body = responseBody };
                }

                // Fire-and-observe -- see this method's own remarks on why
                // this runs unconditionally, from one spot after both
                // branches above.
                extensionHost?.RunResponseAfter(sessionStore.PeekNextId(), targetHost, request, response);
                extensionSession?.ResponseAfter(request, response);

                var session = sessionStore.Add(targetHost, startedAt, request, response, extensionSession?.Flags);
                Console.WriteLine(
                    $"[{startedAt:HH:mm:ss}] #{session.Id} {response.StatusCode} {request.Method} https://{targetHost}{request.Target} " +
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
                    // before it understood HTTP/1.1 at all.
                    await RelayRawBytesUntilClosedAsync(clientTls, upstreamStream, cancellationToken);
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
        SessionStore sessionStore, string targetHost, DateTimeOffset startedAt, CapturedRequest request, string reason)
    {
        var response = new CapturedResponse("HTTP/1.1", 0, reason, [], []);
        var session = sessionStore.Add(targetHost, startedAt, request, response);
        Console.WriteLine($"[{startedAt:HH:mm:ss}] #{session.Id} {request.Method} https://{targetHost}{request.Target} -- {reason}");
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
        AutoResponderOutcome outcome, CapturedRequest request, CancellationToken cancellationToken) => outcome.FinalActionKind switch
    {
        AutoResponderActionKind.ServeFile => BuildServeFileResponseAsync(outcome.Text!, cancellationToken),
        AutoResponderActionKind.ProxyUrl => FetchAndBuildResponseAsync(outcome.Text!, cancellationToken),
        AutoResponderActionKind.Redirect => Task.FromResult(BuildRedirectResponse(outcome.Text!)),
        AutoResponderActionKind.CorsPreflightAllow => Task.FromResult(BuildCorsPreflightResponse(request)),
        _ => throw new InvalidOperationException($"Unexpected AutoResponder final action: {outcome.FinalActionKind}."),
    };

    private static async Task<CapturedResponse> BuildServeFileResponseAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            var missingBody = Encoding.UTF8.GetBytes($"CLeARINET AutoResponder: file not found -- {path}");
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
    private static async Task<CapturedResponse> FetchAndBuildResponseAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var upstreamResponse = await ProxyUrlHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
            var body = Encoding.UTF8.GetBytes($"CLeARINET AutoResponder: fetching '{url}' failed -- {ex.Message}");
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

    private static async Task RelayRawBytesUntilClosedAsync(
        SslStream clientTls, SslStream upstreamTls, CancellationToken cancellationToken)
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

    private static async Task<(string? Method, string? Target)> ReadRequestLineAndHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken);
        if (line is null)
        {
            return (null, null);
        }

        var parts = line.Split(' ', 3);
        var method = parts.Length > 0 ? parts[0] : null;
        var target = parts.Length > 1 ? parts[1] : null;

        // Drain the rest of the request headers up to the blank line;
        // CONNECT never carries a body.
        while (true)
        {
            var headerLine = await ReadLineAsync(stream, cancellationToken);
            if (string.IsNullOrEmpty(headerLine))
            {
                break;
            }
        }

        return (method, target);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Byte-at-a-time on purpose: the only thing read this way is the
        // small, plaintext CONNECT request that precedes the TLS
        // handshake, so simplicity matters more than throughput here.
        var bytes = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(single.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            if (single[0] != (byte)'\r')
            {
                bytes.Add(single[0]);
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
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
