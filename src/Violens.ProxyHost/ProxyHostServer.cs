using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Violens.ProxyCore;
using Violens.ProxyCore.Breakpoints;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Extensions;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Sessions;
using Violens.ProxyCore.SystemProxy;
using Violens.ProxyHost.Client;

namespace Violens.ProxyHost;

/// <summary>
/// The host's side of the pipe: runs ProxyCore on an app's behalf and
/// carries its sessions, hooks and breakpoints over the protocol in
/// <see cref="Protocol"/>. One instance serves one app, for as long as the
/// pipe stays open.
///
/// <see cref="Run"/> reads commands on the calling thread, which should be a
/// dedicated one: hooks block a connection's thread-pool thread until the
/// app answers, and that answer must never wait for the thread pool.
/// </summary>
public sealed class ProxyHostServer
{
    /// <summary>
    /// Bodies larger than this aren't sent over the pipe: a captured
    /// session's body is left out (and the session flagged
    /// <c>x-body-omitted</c>), and a hook isn't called for that message.
    /// Keeps one huge download from swamping the pipe or the app's memory.
    /// </summary>
    public const int MaxBodyBytes = 64 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly string _token;
    private readonly X509Certificate2? _rootForTests;
    private readonly object _writeLock = new();
    private readonly SessionStore _sessions = new();
    private readonly BreakpointManager _breakpoints = new();
    private readonly ConcurrentDictionary<long, PendingBreakpoint> _pendingBreakpoints = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<HookAnswer?>> _pendingHooks = new();
    private long _nextId;
    private volatile bool _connected = true;
    private volatile CaptureSettings? _settings;
    private InterceptingProxyListener? _listener;
    private CertificateAuthority? _authority;
    private bool _authorityTrusted;
    private bool _systemProxyEnabled;
    private UpstreamProxyDetection? _detectedUpstream;

    /// <param name="stream">The pipe to the app.</param>
    /// <param name="token">The token the app launched this host with; sent back first so the app knows it reached the right process.</param>
    /// <param name="rootCertificateForTests">
    /// A root certificate to use instead of the user's real one (created and
    /// trusted by <see cref="CertificateAuthority"/>), so tests never touch
    /// the certificate stores.
    /// </param>
    public ProxyHostServer(Stream stream, string token, X509Certificate2? rootCertificateForTests = null)
    {
        _stream = stream;
        _token = token;
        _rootForTests = rootCertificateForTests;
        _sessions.SessionAdded += OnSessionAdded;
        _breakpoints.BreakpointHit += OnBreakpointHit;
        _breakpoints.BreakpointResolved += OnBreakpointResolved;
    }

    /// <summary>
    /// Says hello, then carries out the app's commands until the pipe
    /// closes. Then stops capturing, puts the system proxy back and lets
    /// anything still waiting on the app go on unchanged.
    /// </summary>
    public void Run()
    {
        try
        {
            Send(MessageType.HostHello, writer =>
            {
                writer.Write(Protocol.Version);
                writer.Write(_token);
                writer.Write(typeof(ProxyHostServer).Assembly.GetName().Version?.ToString() ?? "0.0.0.0");
            });

            while (Protocol.TryReadFrame(_stream, out var type, out var fields))
            {
                Handle(type, fields);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException or OperationCanceledException)
        {
            // The app went away (on Windows, closing the pipe can surface as
            // OperationCanceledException): shut down below.
        }
        finally
        {
            _connected = false;
            StopCapture();
        }
    }

    private void Handle(MessageType type, BinaryReader fields)
    {
        if (type is MessageType.HookReply or MessageType.BreakpointReply)
        {
            // Answers, not commands: nothing to reply to, and a bad one must
            // never end the loop.
            try
            {
                if (type == MessageType.HookReply)
                {
                    OnHookReply(fields);
                }
                else
                {
                    OnBreakpointReply(fields);
                }
            }
            catch (Exception ex) when (ex is not (IOException or ObjectDisposedException or OperationCanceledException))
            {
                SendLog(DateTimeOffset.Now, "host", $"Ignored a {type} the host couldn't use: {ex.Message}");
            }

            return;
        }

        // Everything else is a command, with a request id to answer.
        var id = fields.ReadInt32();
        try
        {
            switch (type)
            {
                case MessageType.Start:
                    Start(id, fields);
                    break;

                case MessageType.Stop:
                    StopCapture();
                    SendOk(id);
                    break;

                case MessageType.SetBreakpoints:
                    SetBreakpoints(fields);
                    SendOk(id);
                    break;

                case MessageType.SaveSaz:
                {
                    // On a worker thread, so hook and breakpoint answers keep
                    // being read while a big file is written.
                    var path = fields.ReadString();
                    RunInBackground(id, () =>
                    {
                        var sessions = _sessions.Snapshot();
                        SazWriter.Write(path, sessions);
                        SendCount(id, sessions.Count);
                    });
                    break;
                }

                case MessageType.LoadSaz:
                {
                    var path = fields.ReadString();
                    RunInBackground(id, () =>
                    {
                        var result = SazReader.ImportAsync(path, _sessions).GetAwaiter().GetResult();
                        foreach (var skipped in result.Skipped)
                        {
                            SendLog(DateTimeOffset.Now, "host", $"Skipped from {Path.GetFileName(path)}: {skipped}");
                        }

                        SendCount(id, result.Imported);
                    });
                    break;
                }

                case MessageType.ClearSessions:
                    _sessions.Clear();
                    SendOk(id);
                    break;

                case MessageType.UninstallRootCertificate:
                    if (_authority is null)
                    {
                        throw new InvalidOperationException("There's no root certificate to remove until capturing has been started.");
                    }

                    _authority.Uninstall();
                    SendOk(id);
                    break;

                default:
                    throw new InvalidOperationException($"This host doesn't know the command {type}.");
            }
        }
        catch (Exception ex)
        {
            // A failed command (a SAZ file that can't be written, say) is
            // reported to the app. If the pipe itself is gone, SendError
            // finds out quietly and the next read ends the loop.
            SendError(id, ex.Message);
        }
    }

    /// <summary>Runs a slow command off the reader thread, answering with an error if it fails.</summary>
    private void RunInBackground(int id, Action command) =>
        _ = Task.Run(() =>
        {
            try
            {
                command();
            }
            catch (Exception ex)
            {
                SendError(id, ex.Message);
            }
        });

    private void Start(int id, BinaryReader fields)
    {
        var productName = fields.ReadString();
        var productVersion = fields.ReadString();
        var rootCertificateName = fields.ReadNullableString();
        var dataFolder = fields.ReadNullableString();
        var preferredPort = fields.ReadInt32();
        var allowRemoteClients = fields.ReadBoolean();
        var useSystemProxy = fields.ReadBoolean();
        var trustRootCertificate = fields.ReadBoolean();
        var hookRequests = fields.ReadBoolean();
        var hookResponses = fields.ReadBoolean();
        var hookTimeoutMs = fields.ReadInt32();
        var includeBodies = fields.ReadBoolean();

        if (_listener is not null)
        {
            throw new InvalidOperationException("Already capturing. Stop first.");
        }

        var identity = new ProxyHostIdentity(productName) { ProductVersion = productVersion, DataFolder = dataFolder };
        if (!string.IsNullOrWhiteSpace(rootCertificateName))
        {
            identity = identity with { RootCertificateName = rootCertificateName };
        }

        // The app's names, not VioLens's, and its log, before anything that
        // uses either (the crash-recovery backup lives in the data folder).
        ProxyEngine.Configure(identity, log: entry => SendLog(entry.Time, entry.Category, entry.Message));
        SystemProxyController.RecoverFromCrash();

        X509Certificate2 root;
        if (_rootForTests is not null)
        {
            root = _rootForTests;
        }
        else
        {
            // Created again when the name changes, or when trust is wanted
            // now and wasn't before (creating it is what trusts it).
            if (_authority is null || _authority.RootCertificateName != identity.RootCertificateName || (trustRootCertificate && !_authorityTrusted))
            {
                _authority = new CertificateAuthority(installToTrustStore: trustRootCertificate);
                _authorityTrusted = trustRootCertificate;
            }

            root = _authority.RootCertificate;
        }

        _settings = new CaptureSettings(hookRequests, hookResponses, TimeSpan.FromMilliseconds(Math.Max(1, hookTimeoutMs)), includeBodies);
        var listener = InterceptingProxyListener.StartOnAvailablePort(
            preferredPort,
            new LeafCertificateProvider(root),
            _sessions,
            breakpointManager: _breakpoints,
            sessionHost: new RemoteHooks(this),
            allowRemoteClients: allowRemoteClients);
        _listener = listener;

        string? upstream = null;
        if (useSystemProxy)
        {
            // The user's own proxy, or their automatic configuration (PAC
            // or WPAD) script, as VioLens itself does.
            var detected = SystemProxyController.DetectUpstream(listener.Port);
            _detectedUpstream = detected;
            listener.Gateway = detected.Proxy;
            listener.AutoConfig = detected.AutoConfig;
            upstream = detected.Description;
            SystemProxyController.Enable(listener.Port);
            _systemProxyEnabled = true;
        }

        Send(MessageType.Started, writer =>
        {
            writer.Write(id);
            writer.Write(listener.Port);
            writer.WriteBytes(root.Export(X509ContentType.Cert));
            writer.WriteNullableString(upstream);
        });
    }

    private void StopCapture()
    {
        var listener = _listener;
        _listener = null;
        listener?.Stop();

        // The listener doesn't own a PAC script it was given; this does.
        _detectedUpstream?.AutoConfig?.Dispose();
        _detectedUpstream = null;

        if (_systemProxyEnabled)
        {
            _systemProxyEnabled = false;
            try
            {
                SystemProxyController.Disable();
            }
            catch (Exception ex)
            {
                SendLog(DateTimeOffset.Now, "system-proxy", $"Couldn't put the system proxy back: {ex.Message}");
            }
        }

        // Nothing should stay paused waiting for an app that's stopped listening.
        foreach (var id in _pendingBreakpoints.Keys)
        {
            if (_pendingBreakpoints.TryRemove(id, out var pending))
            {
                pending.Resume();
            }
        }

        if (!_connected)
        {
            foreach (var id in _pendingHooks.Keys)
            {
                if (_pendingHooks.TryRemove(id, out var hook))
                {
                    hook.TrySetResult(null);
                }
            }
        }
    }

    private void SetBreakpoints(BinaryReader fields)
    {
        var rules = _breakpoints.Rules;
        rules.BreakOnAllRequests = fields.ReadBoolean();
        rules.BreakOnAllResponses = fields.ReadBoolean();
        rules.RequestUrlContains = fields.ReadNullableString();
        rules.ResponseUrlContains = fields.ReadNullableString();
        rules.RequestMethodEquals = fields.ReadNullableString();
        var hasStatus = fields.ReadBoolean();
        var status = fields.ReadInt32();
        rules.ResponseStatusCodeEquals = hasStatus ? status : null;
    }

    // --- Sessions and breakpoints ----------------------------------------

    private void OnSessionAdded(Session session)
    {
        if (!_connected)
        {
            return;
        }

        var includeBodies = _settings?.IncludeBodies ?? true;
        var tooBig = session.Request.Body.Length > MaxBodyBytes || session.Response.Body.Length > MaxBodyBytes;
        var captured = new CapturedSession
        {
            Id = session.Id,
            Host = session.Host,
            Scheme = session.Scheme,
            Url = SessionUrl.Of(session),
            StartedAt = session.StartedAt,
            Request = ToClient(session.Request, includeBodies && session.Request.Body.Length <= MaxBodyBytes),
            Response = ToClient(session.Response, includeBodies && session.Response.Body.Length <= MaxBodyBytes),
        };

        if (session.Flags is not null)
        {
            foreach (var flag in session.Flags)
            {
                captured.Flags[flag.Key] = flag.Value;
            }
        }

        if (includeBodies && tooBig)
        {
            captured.Flags["x-body-omitted"] = $"Bodies over {MaxBodyBytes / (1024 * 1024)} MB aren't sent to the app.";
        }

        TrySend(MessageType.SessionCaptured, writer => writer.WriteSession(captured));
    }

    private void OnBreakpointHit(PendingBreakpoint pending)
    {
        if (!_connected)
        {
            pending.Resume();
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        _pendingBreakpoints[id] = pending;
        var sent = TrySend(MessageType.BreakpointHit, writer =>
        {
            writer.Write(id);
            writer.Write(pending.SessionOrdinal);
            writer.Write((byte)(pending.Stage == BreakpointStage.Request ? ExchangeStage.Request : ExchangeStage.Response));
            writer.Write(pending.Host);
            writer.Write(pending.RawText);
        });

        if (!sent && _pendingBreakpoints.TryRemove(id, out _))
        {
            pending.Resume();
        }
    }

    /// <summary>
    /// A paused message that's no longer waiting (its client disconnected,
    /// or it was answered): forget it, so a late answer from the app is
    /// ignored.
    /// </summary>
    private void OnBreakpointResolved(PendingBreakpoint resolved)
    {
        foreach (var entry in _pendingBreakpoints)
        {
            if (ReferenceEquals(entry.Value, resolved))
            {
                _pendingBreakpoints.TryRemove(entry.Key, out _);
            }
        }
    }

    private void OnBreakpointReply(BinaryReader fields)
    {
        var id = fields.ReadInt64();
        var resume = fields.ReadBoolean();
        var edited = fields.ReadNullableString();
        if (!_pendingBreakpoints.TryRemove(id, out var pending))
        {
            return; // Already let go (stopped, or answered twice).
        }

        if (!resume)
        {
            pending.Abort();
            return;
        }

        var original = pending.RawText;
        if (edited is not null && !pending.TryEdit(edited))
        {
            // TryEdit keeps invalid text (for an editor to go on showing);
            // put the original back so the message can go on unchanged.
            var error = pending.ValidationError;
            pending.TryEdit(original);
            SendLog(DateTimeOffset.Now, "host", $"The edit to paused message #{pending.SessionOrdinal} wasn't valid HTTP ({error}), so it went on unchanged.");
        }

        pending.Resume();
    }

    // --- Hooks ------------------------------------------------------------

    internal CaptureSettings? Settings => _settings;

    internal bool IsConnected => _connected;

    /// <summary>
    /// Asks the app to run a hook and waits for its answer, up to the
    /// configured timeout. Null means "go on unchanged": no answer in time,
    /// or the app went away.
    /// </summary>
    internal HookAnswer? CallHook(ExchangeStage stage, int sessionId, string host, string scheme, CapturedRequest request, CapturedResponse? response, IDictionary<string, string> flags)
    {
        var settings = _settings;
        if (!_connected || settings is null)
        {
            return null;
        }

        if (request.Body.Length > MaxBodyBytes || (response?.Body.Length ?? 0) > MaxBodyBytes)
        {
            SendLog(DateTimeOffset.Now, "host", $"#{sessionId}'s body is over {MaxBodyBytes / (1024 * 1024)} MB, so the app's hook wasn't called for it.");
            return null;
        }

        var id = Interlocked.Increment(ref _nextId);
        var answer = new TaskCompletionSource<HookAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingHooks[id] = answer;
        try
        {
            var sent = TrySend(MessageType.HookRequest, writer =>
            {
                writer.Write(id);
                writer.Write((byte)stage);
                writer.Write(sessionId);
                writer.Write(host);
                writer.Write(scheme);
                writer.WriteRequest(ToClient(request, includeBody: true));
                writer.WriteNullableResponse(response is null ? null : ToClient(response, includeBody: true));
                writer.WriteFlags(flags);
            });

            if (!sent)
            {
                return null;
            }

            if (!answer.Task.Wait(settings.HookTimeout))
            {
                SendLog(DateTimeOffset.Now, "host",
                    $"The app's {(stage == ExchangeStage.Request ? "OnBeforeRequest" : "OnBeforeResponse")} hook didn't answer within {settings.HookTimeout.TotalSeconds:0.#} s for #{sessionId}, so it went on unchanged.");
                return null;
            }

            return answer.Task.Result;
        }
        finally
        {
            _pendingHooks.TryRemove(id, out _);
        }
    }

    private void OnHookReply(BinaryReader fields)
    {
        var id = fields.ReadInt64();
        var request = fields.ReadRequest();
        var response = fields.ReadNullableResponse();
        var localResponse = fields.ReadNullableResponse();
        var flags = fields.ReadFlags();
        if (_pendingHooks.TryGetValue(id, out var answer))
        {
            answer.TrySetResult(new HookAnswer(FromClient(request), response is null ? null : FromClient(response), localResponse is null ? null : FromClient(localResponse), flags));
        }
    }

    // --- Conversions ------------------------------------------------------

    internal static ProxyRequest ToClient(CapturedRequest request, bool includeBody)
    {
        var result = new ProxyRequest { Method = request.Method, Target = request.Target, HttpVersion = request.HttpVersion };
        result.Headers = request.Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).ToList();
        result.Body = includeBody ? request.Body : [];
        return result;
    }

    internal static ProxyResponse ToClient(CapturedResponse response, bool includeBody)
    {
        var result = new ProxyResponse { HttpVersion = response.HttpVersion, StatusCode = response.StatusCode, ReasonPhrase = response.ReasonPhrase };
        result.Headers = response.Headers.Select(h => new KeyValuePair<string, string>(h.Name, h.Value)).ToList();
        result.Body = includeBody ? response.Body : [];
        return result;
    }

    internal static CapturedRequest FromClient(ProxyRequest request) =>
        new(request.Method, request.Target, request.HttpVersion,
            request.Headers.Select(h => (h.Key, h.Value)).ToList(), request.Body ?? []);

    internal static CapturedResponse FromClient(ProxyResponse response) =>
        new(response.HttpVersion, response.StatusCode, response.ReasonPhrase,
            response.Headers.Select(h => (h.Key, h.Value)).ToList(), response.Body ?? []);

    // --- Sending ----------------------------------------------------------

    private void Send(MessageType type, Action<BinaryWriter> writeFields) =>
        Protocol.WriteFrame(_stream, _writeLock, type, writeFields);

    /// <summary>Sends, or returns false if the app has gone away. For messages sent from connection threads.</summary>
    private bool TrySend(MessageType type, Action<BinaryWriter> writeFields)
    {
        if (!_connected)
        {
            return false;
        }

        try
        {
            Send(type, writeFields);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _connected = false;
            return false;
        }
    }

    private void SendOk(int id) => Send(MessageType.Ok, writer => writer.Write(id));

    private void SendError(int id, string message) =>
        TrySend(MessageType.Error, writer =>
        {
            writer.Write(id);
            writer.Write(message);
        });

    private void SendCount(int id, int count) =>
        Send(MessageType.Count, writer =>
        {
            writer.Write(id);
            writer.Write(count);
        });

    private void SendLog(DateTimeOffset time, string category, string message) =>
        TrySend(MessageType.Log, writer =>
        {
            writer.Write(time.ToUnixTimeMilliseconds());
            writer.Write(category);
            writer.Write(message);
        });
}

/// <summary>What the app asked for when it started capturing, as far as hooks and sessions go.</summary>
internal sealed record CaptureSettings(bool HookRequests, bool HookResponses, TimeSpan HookTimeout, bool IncludeBodies);

/// <summary>The app's answer to a hook.</summary>
internal sealed record HookAnswer(CapturedRequest Request, CapturedResponse? Response, CapturedResponse? LocalResponse, Dictionary<string, string> Flags);

/// <summary>
/// ProxyCore's per-request hooks, answered by the app over the pipe. Only
/// active while the app asked for request or response hooks.
/// </summary>
internal sealed class RemoteHooks(ProxyHostServer server) : IExtensionSessionHost
{
    public bool IsActive =>
        server.IsConnected && server.Settings is { } settings && (settings.HookRequests || settings.HookResponses);

    public IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme) =>
        new RemoteSession(server, sessionOrdinal, hostname, scheme);

    private sealed class RemoteSession(ProxyHostServer server, int sessionId, string host, string scheme) : IExtensionSession
    {
        private Dictionary<string, string> _flags = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string>? Flags =>
            _flags.Count == 0 ? null : new Dictionary<string, string>(_flags, StringComparer.OrdinalIgnoreCase);

        public void PeekAtRequestHeaders(CapturedRequest requestPreamble)
        {
        }

        public ExtensionRequestResult RequestBefore(CapturedRequest request)
        {
            if (server.Settings is not { HookRequests: true })
            {
                return new ExtensionRequestResult(request);
            }

            var answer = server.CallHook(ExchangeStage.Request, sessionId, host, scheme, request, null, _flags);
            if (answer is null)
            {
                return new ExtensionRequestResult(request);
            }

            _flags = new Dictionary<string, string>(answer.Flags, StringComparer.OrdinalIgnoreCase);
            return new ExtensionRequestResult(answer.Request, answer.LocalResponse);
        }

        public void RequestAfter(CapturedRequest request)
        {
        }

        public void PeekAtResponseHeaders(CapturedRequest request, CapturedResponse responsePreamble)
        {
        }

        public CapturedResponse ResponseBefore(CapturedRequest request, CapturedResponse response)
        {
            if (server.Settings is not { HookResponses: true })
            {
                return response;
            }

            var answer = server.CallHook(ExchangeStage.Response, sessionId, host, scheme, request, response, _flags);
            if (answer is null)
            {
                return response;
            }

            _flags = new Dictionary<string, string>(answer.Flags, StringComparer.OrdinalIgnoreCase);
            return answer.Response ?? response;
        }

        public void ResponseAfter(CapturedRequest request, CapturedResponse response)
        {
        }
    }
}
