using System;
using System.Collections.Generic;

namespace Violens.ProxyHost.Client
{
    /// <summary>
    /// Who the app is, so the proxy engine names what it creates after it
    /// (the same settings as ProxyCore's <c>ProxyHostIdentity</c>). Only
    /// <see cref="ProductName"/> is required; the rest are derived from it
    /// when left null.
    /// </summary>
    public sealed class AppIdentity
    {
        public AppIdentity(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName))
            {
                throw new ArgumentException("A product name is required.", nameof(productName));
            }

            ProductName = productName;
        }

        /// <summary>The app's name. "My App" gives a root certificate named <c>DO_NOT_TRUST_MyAppRoot</c>, files under <c>%LOCALAPPDATA%\My App</c>, and the proxy page <c>http://myapp/</c>.</summary>
        public string ProductName { get; }

        /// <summary>The app's version, written into HAR files. Optional.</summary>
        public string ProductVersion { get; set; } = string.Empty;

        /// <summary>The root certificate's name, to keep one the app already uses. Null derives it from the product name.</summary>
        public string? RootCertificateName { get; set; }

        /// <summary>The folder for the engine's own files (the system-proxy crash-recovery backup). Null derives it from the product name.</summary>
        public string? DataFolder { get; set; }
    }

    /// <summary>How <see cref="ProxyHostClient.StartAsync"/> starts capturing.</summary>
    public sealed class StartOptions
    {
        /// <summary>The port to listen on. If it's in use, or is 0, the system picks a free one; <see cref="StartResult.Port"/> says which.</summary>
        public int PreferredPort { get; set; } = 8888;

        /// <summary>Accept connections from other devices (phones, other computers), not just this one.</summary>
        public bool AllowRemoteClients { get; set; }

        /// <summary>
        /// Become the system proxy while capturing (the user's own proxy
        /// settings are put back on stop, or after a crash), and forward
        /// through whatever proxy the user had before.
        /// </summary>
        public bool UseSystemProxy { get; set; }

        /// <summary>
        /// Make sure the root certificate is trusted, which shows the
        /// system's prompt the first time on Windows. On macOS there's no
        /// system prompt, so ask the user before setting this. When false,
        /// HTTPS sites won't trust the proxy until the certificate is
        /// trusted some other way.
        /// </summary>
        public bool TrustRootCertificate { get; set; } = true;

        /// <summary>Call <see cref="ProxyHostClient.OnBeforeRequest"/> for every request. Requests then wait for it.</summary>
        public bool HookRequests { get; set; }

        /// <summary>Call <see cref="ProxyHostClient.OnBeforeResponse"/> for every response. Responses then wait for it.</summary>
        public bool HookResponses { get; set; }

        /// <summary>
        /// How long the host waits for a hook before letting the request or
        /// response go on unchanged. A hook that takes longer is logged, and
        /// its late answer ignored.
        /// </summary>
        public TimeSpan HookTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Include request and response bodies in <see cref="ProxyHostClient.SessionCaptured"/>. Turn off to keep memory and pipe traffic down.</summary>
        public bool IncludeBodies { get; set; } = true;
    }

    /// <summary>What <see cref="ProxyHostClient.StartAsync"/> started.</summary>
    public sealed class StartResult
    {
        internal StartResult(int port, byte[] rootCertificate, string? upstream)
        {
            Port = port;
            RootCertificate = rootCertificate;
            UpstreamDescription = upstream;
        }

        /// <summary>The port being listened on.</summary>
        public int Port { get; }

        /// <summary>The root certificate (DER-encoded), to show or export. Load it with <c>new X509Certificate2(bytes)</c>.</summary>
        public byte[] RootCertificate { get; }

        /// <summary>When the system proxy was taken over, how traffic leaves this computer (directly, or through which proxy). Otherwise null.</summary>
        public string? UpstreamDescription { get; }
    }

    /// <summary>
    /// When to pause a request or response for editing. Each setting is its
    /// own trigger: any one matching pauses the message. All off (the
    /// default) pauses nothing.
    /// </summary>
    public sealed class BreakpointSettings
    {
        public bool BreakOnAllRequests { get; set; }
        public bool BreakOnAllResponses { get; set; }

        /// <summary>Pause requests whose URL contains this text (QuickExec's <c>bpu</c>).</summary>
        public string? RequestUrlContains { get; set; }

        /// <summary>Pause responses to requests whose URL contains this text (<c>bpafter</c>).</summary>
        public string? ResponseUrlContains { get; set; }

        /// <summary>Pause requests with this method (<c>bpm</c>).</summary>
        public string? RequestMethodEquals { get; set; }

        /// <summary>Pause responses with this status code (<c>bps</c>).</summary>
        public int? ResponseStatusCodeEquals { get; set; }
    }

    /// <summary>Which half of an exchange a hook or breakpoint is about.</summary>
    public enum ExchangeStage
    {
        Request = 1,
        Response = 2,
    }

    /// <summary>
    /// What a hook sees and changes. Change <see cref="Request"/> or
    /// <see cref="Response"/> in place, set <see cref="Flags"/>, or (for a
    /// request) call <see cref="RespondWith"/> to answer it without contacting
    /// the server, like Fiddler's <c>utilCreateResponseAndBypassServer</c>.
    /// </summary>
    public sealed class HookContext
    {
        internal HookContext(ExchangeStage stage, int sessionId, string host, string scheme, ProxyRequest request, ProxyResponse? response, Dictionary<string, string> flags)
        {
            Stage = stage;
            SessionId = sessionId;
            Host = host;
            Scheme = scheme;
            Request = request;
            Response = response;
            Flags = flags;
        }

        public ExchangeStage Stage { get; }

        /// <summary>The number the session will most likely get once captured.</summary>
        public int SessionId { get; }

        public string Host { get; }

        public string Scheme { get; }

        /// <summary>The request. In a request hook, changes are sent to the server.</summary>
        public ProxyRequest Request { get; }

        /// <summary>The response, in a response hook; changes are returned to the client. Null in a request hook.</summary>
        public ProxyResponse? Response { get; }

        /// <summary>Fiddler-style string flags, kept with the session (<c>ui-backcolor</c>, <c>ui-comments</c>...).</summary>
        public Dictionary<string, string> Flags { get; }

        internal ProxyResponse? LocalResponse { get; private set; }

        /// <summary>Answers the request with <paramref name="response"/> instead of sending it to the server. Request hooks only.</summary>
        public void RespondWith(ProxyResponse response)
        {
            if (Stage != ExchangeStage.Request)
            {
                throw new InvalidOperationException("Only a request hook can answer the request itself.");
            }

            LocalResponse = response ?? throw new ArgumentNullException(nameof(response));
        }
    }

    /// <summary>A captured session, for <see cref="ProxyHostClient.SessionCaptured"/>.</summary>
    public sealed class SessionCapturedEventArgs : EventArgs
    {
        internal SessionCapturedEventArgs(CapturedSession session) => Session = session;

        public CapturedSession Session { get; }
    }

    /// <summary>A message from the engine's log, for <see cref="ProxyHostClient.Log"/>.</summary>
    public sealed class ProxyLogEventArgs : EventArgs
    {
        internal ProxyLogEventArgs(DateTimeOffset time, string category, string message)
        {
            Time = time;
            Category = category;
            Message = message;
        }

        public DateTimeOffset Time { get; }

        /// <summary><c>proxy</c>, <c>session</c>, <c>system-proxy</c>, <c>pac</c> or <c>host</c>.</summary>
        public string Category { get; }

        public string Message { get; }

        public override string ToString() => "[" + Category + "] " + Message;
    }

    /// <summary>
    /// A paused request or response, for <see cref="ProxyHostClient.BreakpointHit"/>.
    /// Call <see cref="Resume"/> or <see cref="Abort"/> exactly once, from any
    /// thread, whenever the user is done; until then that one message waits
    /// and everything else keeps flowing.
    /// </summary>
    public sealed class BreakpointEventArgs : EventArgs
    {
        private readonly Action<bool, string?> _reply;
        private int _replied;

        internal BreakpointEventArgs(int sessionOrdinal, ExchangeStage stage, string host, string rawText, Action<bool, string?> reply)
        {
            SessionOrdinal = sessionOrdinal;
            Stage = stage;
            Host = host;
            RawText = rawText;
            _reply = reply;
        }

        public int SessionOrdinal { get; }

        public ExchangeStage Stage { get; }

        public string Host { get; }

        /// <summary>The paused message as raw HTTP text, for an editor.</summary>
        public string RawText { get; }

        /// <summary>
        /// Lets the message go on: unchanged when <paramref name="editedRawText"/>
        /// is null, otherwise as edited. An edit that isn't valid HTTP is
        /// ignored (logged by the host) and the original goes on.
        /// </summary>
        public void Resume(string? editedRawText = null) => Reply(true, editedRawText);

        /// <summary>Drops the message: the client's connection is closed.</summary>
        public void Abort() => Reply(false, null);

        private void Reply(bool resume, string? editedRawText)
        {
            if (System.Threading.Interlocked.Exchange(ref _replied, 1) == 0)
            {
                _reply(resume, editedRawText);
            }
        }
    }
}
