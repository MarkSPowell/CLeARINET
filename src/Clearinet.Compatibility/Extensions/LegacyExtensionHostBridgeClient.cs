using System.IO.Pipes;
using Clearinet.LegacyExtensionHost.Bridge;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// The main app's side of the session bridge (see the design doc's
/// "Session bridge" section) -- an <see cref="IExtensionAutoTamperHost"/>
/// that runs every loaded legacy extension's <c>Fiddler.IAutoTamper</c>
/// hooks by calling out to a separately-running
/// <c>Clearinet.LegacyExtensionHost.exe</c> process over a named pipe
/// (<see cref="SessionBridgeProtocol"/>), rather than in-process the way
/// <see cref="LoadedExtensionSet"/> runs compiled, source-compatible
/// extensions.
///
/// <b>Never a hard dependency.</b> Nothing about running CLeARINET's main
/// app, or proxying real traffic, requires the legacy host process to
/// exist -- <see cref="Probe"/> is the only place this class ever tries to
/// reach it, once per proxy <c>Start()</c> (see
/// <c>MainWindowViewModel.Start</c>), with a short timeout
/// (<see cref="SessionBridgeProtocol.ConnectTimeoutMilliseconds"/>); if
/// that fails, every hook method below becomes an unconditional passthrough
/// for the rest of that run, with no further connection attempts. That's a
/// deliberate difference from <see cref="ExtensionHost"/>'s own
/// "loaded once, at app startup, never reloaded" scope: probing fresh on
/// every <c>Start()</c> click, rather than once at app launch, means
/// launching the legacy host .exe *after* opening CLeARINET still works,
/// the next time Start is clicked -- no restart of the main app required.
///
/// <b>One pipe connection per hook call, not one long-lived connection.</b>
/// Simpler to reason about correctly than a persistent pipe shared across
/// however many connections <see cref="Proxy.InterceptingProxyListener"/>
/// may be juggling concurrently (no request/response correlation needed,
/// no risk of one caller's bytes interleaving with another's on a stream
/// two threads write to at once) -- see
/// <c>SessionBridgeServer.InstanceCount</c> for the matching choice on the
/// legacy host's own side. The cost is a fresh connect per call rather than
/// a warm one; for local, same-machine named-pipe IPC this is small enough
/// not to matter for interactively browsing through the proxy.
/// </summary>
public sealed class LegacyExtensionHostBridgeClient : IExtensionAutoTamperHost
{
    private readonly Action<string> _log;
    private readonly bool _reachable;

    private LegacyExtensionHostBridgeClient(bool reachable, Action<string> log)
    {
        _reachable = reachable;
        _log = log;
    }

    /// <summary>
    /// Tries once, synchronously, to reach a running legacy host and asks
    /// it how many <c>IAutoTamper</c> extensions it has loaded
    /// (<see cref="BridgeMessageKind.Capabilities"/>). Always returns an
    /// instance -- never throws -- so call sites don't need their own
    /// try/catch; an unreachable host, or one with nothing loaded, just
    /// means every hook below becomes a no-op passthrough (see this
    /// class's own remarks).
    /// </summary>
    public static LegacyExtensionHostBridgeClient Probe(Action<string>? log = null)
    {
        log ??= _ => { };

        var response = Exchange(new BridgeRequestMessage { Kind = BridgeMessageKind.Capabilities }, log);
        if (response is { Ok: true, AutoTamperCount: > 0 })
        {
            log($"[LegacyExtensionHost] Session bridge reachable -- {response.AutoTamperCount} AutoTamper extension(s) loaded there.");
            return new LegacyExtensionHostBridgeClient(reachable: true, log);
        }

        if (response is { Ok: true })
        {
            log("[LegacyExtensionHost] Session bridge reachable, but no AutoTamper extensions are loaded there -- nothing to bridge this run.");
        }
        // A null/failed response already logged its own reason inside
        // Exchange (connect timeout, or the pipe wasn't there at all) --
        // nothing more to say here.

        return new LegacyExtensionHostBridgeClient(reachable: false, log);
    }

    /// <summary>
    /// A cheaper, more literal reachability check than <see cref="Probe"/>:
    /// "is anything at all listening on the session bridge pipe right now,"
    /// with no opinion about how many (if any) <c>IAutoTamper</c> extensions
    /// it has loaded. <see cref="Probe"/> deliberately treats a reachable
    /// host with zero loaded AutoTampers the same as an unreachable one
    /// (see its own remarks -- there's nothing useful to bridge either way),
    /// which makes it the wrong check for <see cref="LegacyExtensionHostLauncher"/>'s
    /// "should I spawn a second copy of the process" decision: a legacy
    /// host that's genuinely running, just with no AutoTamper-implementing
    /// extension dropped in yet, is still a process that already exists and
    /// must not be duplicated.
    /// </summary>
    public static bool Ping(Action<string>? log = null)
    {
        log ??= _ => { };
        var response = Exchange(new BridgeRequestMessage { Kind = BridgeMessageKind.Capabilities }, log);
        return response is { Ok: true };
    }

    /// <inheritdoc/>
    public bool HasAnyRequestBeforeHandlers => _reachable;

    /// <inheritdoc/>
    public bool HasAnyResponseBeforeHandlers => _reachable;

    /// <inheritdoc/>
    public CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (!_reachable)
        {
            return request;
        }

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestBefore,
            SessionOrdinal = sessionOrdinal,
            Hostname = hostname,
            Request = ToWire(request),
        };

        var response = Exchange(message, _log);
        return response is { Ok: true, Request: not null } ? FromWire(response.Request) : request;
    }

    /// <inheritdoc/>
    public void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request)
    {
        if (!_reachable)
        {
            return;
        }

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.RequestAfter,
            SessionOrdinal = sessionOrdinal,
            Hostname = hostname,
            Request = ToWire(request),
        };

        // Result deliberately unused -- fire-and-observe, matching
        // IExtensionAutoTamperHost.RunRequestAfter's own void contract (see
        // that interface's remarks: by this point the request has already
        // gone out over the wire, so nothing here can change what was sent).
        Exchange(message, _log);
    }

    /// <inheritdoc/>
    public CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (!_reachable)
        {
            return response;
        }

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.ResponseBefore,
            SessionOrdinal = sessionOrdinal,
            Hostname = hostname,
            Request = ToWire(request),
            Response = ToWire(response),
        };

        var result = Exchange(message, _log);
        return result is { Ok: true, Response: not null } ? FromWire(result.Response) : response;
    }

    /// <inheritdoc/>
    public void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response)
    {
        if (!_reachable)
        {
            return;
        }

        var message = new BridgeRequestMessage
        {
            Kind = BridgeMessageKind.ResponseAfter,
            SessionOrdinal = sessionOrdinal,
            Hostname = hostname,
            Request = ToWire(request),
            Response = ToWire(response),
        };

        Exchange(message, _log);
    }

    /// <summary>
    /// One full pipe round trip: connect (bounded by
    /// <see cref="SessionBridgeProtocol.ConnectTimeoutMilliseconds"/>,
    /// which <see cref="NamedPipeClientStream.Connect(int)"/> already
    /// enforces on its own -- the common, cheap-to-detect "no legacy host
    /// running" case), then write the request and read the response
    /// (bounded by <see cref="SessionBridgeProtocol.RoundTripTimeoutMilliseconds"/>,
    /// enforced here explicitly since <see cref="PipeStream"/>'s
    /// synchronous Read/Write have no built-in timeout of their own).
    /// Never throws -- every failure is caught, logged once, and reported
    /// as <c>null</c> so every call site above can fail open (pass the
    /// original, unmodified request/response through) rather than ever
    /// breaking real browsing traffic over a legacy-extension problem.
    /// </summary>
    private static BridgeResponseMessage? Exchange(BridgeRequestMessage message, Action<string> log)
    {
        NamedPipeClientStream? pipe = null;
        try
        {
            pipe = new NamedPipeClientStream(".", SessionBridgeProtocol.PipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(SessionBridgeProtocol.ConnectTimeoutMilliseconds);

            var localPipe = pipe;
            var exchangeTask = Task.Run(() =>
            {
                SessionBridgeProtocol.WriteMessage(localPipe, message);
                return SessionBridgeProtocol.ReadMessage<BridgeResponseMessage>(localPipe);
            });

            if (!exchangeTask.Wait(SessionBridgeProtocol.RoundTripTimeoutMilliseconds))
            {
                // Tear the pipe down from this thread to force-unblock
                // whatever synchronous Read/Write exchangeTask is still
                // stuck in -- PipeStream gives a blocked synchronous call no
                // other way to be cancelled. exchangeTask's own resulting
                // exception, once it does unblock, is never observed; that's
                // fine, this call has already decided to fail open.
                localPipe.Dispose();
                throw new TimeoutException($"Session bridge round trip for {message.Kind} did not complete within {SessionBridgeProtocol.RoundTripTimeoutMilliseconds}ms.");
            }

            return exchangeTask.Result;
        }
        catch (Exception ex)
        {
            log($"[LegacyExtensionHost] Session bridge call ({message.Kind}) failed -- falling back to the unmodified request/response: {ex.Message}");
            return null;
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private static WireRequest ToWire(CapturedRequest request) => new()
    {
        Method = request.Method,
        Target = request.Target,
        HttpVersion = request.HttpVersion,
        Headers = request.Headers.Select(h => new WireHeader { Name = h.Name, Value = h.Value }).ToList(),
        Body = request.Body,
    };

    private static CapturedRequest FromWire(WireRequest wire) => new(
        wire.Method,
        wire.Target,
        wire.HttpVersion,
        wire.Headers.Select(h => (h.Name, h.Value)).ToList(),
        wire.Body);

    private static WireResponse ToWire(CapturedResponse response) => new()
    {
        HttpVersion = response.HttpVersion,
        StatusCode = response.StatusCode,
        ReasonPhrase = response.ReasonPhrase,
        Headers = response.Headers.Select(h => new WireHeader { Name = h.Name, Value = h.Value }).ToList(),
        Body = response.Body,
    };

    private static CapturedResponse FromWire(WireResponse wire) => new(
        wire.HttpVersion,
        wire.StatusCode,
        wire.ReasonPhrase,
        wire.Headers.Select(h => (h.Name, h.Value)).ToList(),
        wire.Body);
}
