using System.Collections;
using Clearinet.ProxyCore.Extensions;
using Clearinet.ProxyCore.Http;
using ShimAutoTamper = Clearinet.CompatShim.IAutoTamper;
using ShimAutoTamper2 = Clearinet.CompatShim.IAutoTamper2;
using ShimAutoTamper3 = Clearinet.CompatShim.IAutoTamper3;
using ShimRequestHeaders = Clearinet.CompatShim.HTTPRequestHeaders;
using ShimResponseHeaders = Clearinet.CompatShim.HTTPResponseHeaders;
using ShimSession = Clearinet.CompatShim.Session;
using ShimSessionFlags = Clearinet.CompatShim.SessionFlags;

namespace Clearinet.Compatibility.Extensions;

/// <summary>
/// Runs ported Fiddler-shaped extensions' <c>IAutoTamper</c> hooks
/// (<see cref="ShimAutoTamper"/> and its <c>2</c>/<c>3</c> variants) against
/// live traffic, with one <see cref="ShimSession"/> per request that lives
/// across every hook, as in Fiddler Classic. So a flag set in
/// <c>AutoTamperRequestBefore</c> is still there in
/// <c>AutoTamperResponseBefore</c>, ends up on the recorded session's Notes
/// tab, and a response created with
/// <see cref="ShimSession.utilCreateResponseAndBypassServer"/> is returned
/// without contacting the server.
///
/// Each hook runs every loaded extension in load order, each one seeing the
/// previous one's edits. One that throws is logged and skipped; the rest
/// still run, and traffic keeps flowing.
///
/// Between hooks, FiddlerScript or a breakpoint may replace the request or
/// response. The session picks those changes up (see
/// <see cref="Scope.SyncRequest"/>) but keeps its flags and, when nothing
/// changed, its very same header objects.
/// </summary>
public sealed class ShimAutoTamperSet : IExtensionSessionHost
{
    private readonly IReadOnlyList<ShimAutoTamper> _tampers;
    private readonly Action<string> _log;

    public ShimAutoTamperSet(IReadOnlyList<ShimAutoTamper> tampers, Action<string>? log = null)
    {
        _tampers = tampers;
        _log = log ?? (_ => { });
    }

    public bool IsActive => _tampers.Count > 0;

    public IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme) => new Scope(this, hostname, scheme);

    private void RunEach(string hookName, Action<ShimAutoTamper> hook)
    {
        foreach (var tamper in _tampers)
        {
            try
            {
                hook(tamper);
            }
            catch (Exception ex)
            {
                _log($"[Extension] {tamper.GetType().FullName}.{hookName} threw: {ex.Message}");
            }
        }
    }

    /// <summary>One request's session and hooks.</summary>
    private sealed class Scope : IExtensionSession
    {
        private readonly ShimAutoTamperSet _owner;
        private readonly string _hostname;
        private readonly string _scheme;
        private ShimSession? _session;

        // The last request/response this scope handed back (or was given).
        // When the listener passes the same instance in again, nothing
        // changed in between, so the session is left exactly as the
        // extensions left it.
        private CapturedRequest? _syncedRequest;
        private CapturedResponse? _syncedResponse;

        // Header edits made in the peek hooks: the headers as peeked, and as
        // the extensions left them. Applied when the full message arrives
        // with the headers still as peeked (see WithPeekEdits).
        private IReadOnlyList<(string Name, string Value)>? _requestPeeked;
        private IReadOnlyList<(string Name, string Value)>? _requestPeekEdited;
        private IReadOnlyList<(string Name, string Value)>? _responsePeeked;
        private IReadOnlyList<(string Name, string Value)>? _responsePeekEdited;

        public Scope(ShimAutoTamperSet owner, string hostname, string scheme)
        {
            _owner = owner;
            _hostname = hostname;
            _scheme = scheme;
        }

        public IReadOnlyDictionary<string, string>? Flags
        {
            get
            {
                if (_session is null || _session.oFlags.Count == 0)
                {
                    return null;
                }

                var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in _session.oFlags)
                {
                    if (entry.Key is string name && entry.Value is string value)
                    {
                        flags[name] = value;
                    }
                }

                return flags;
            }
        }

        public void PeekAtRequestHeaders(CapturedRequest requestPreamble)
        {
            var session = SyncRequest(requestPreamble);
            _owner.RunEach(nameof(ShimAutoTamper3.OnPeekAtRequestHeaders), tamper =>
            {
                if (tamper is ShimAutoTamper3 peeker)
                {
                    peeker.OnPeekAtRequestHeaders(session);
                }
            });

            // The body hasn't arrived yet. Keep any header edits for when it
            // has, and forget what was synced so the full request replaces it.
            _requestPeeked = requestPreamble.Headers;
            _requestPeekEdited = HeadersOf(session.oRequest.headers);
            _syncedRequest = null;
        }

        public ExtensionRequestResult RequestBefore(CapturedRequest request)
        {
            var session = SyncRequest(WithPeekEdits(request, ref _requestPeeked, ref _requestPeekEdited));
            _owner.RunEach(nameof(ShimAutoTamper.AutoTamperRequestBefore), tamper => tamper.AutoTamperRequestBefore(session));

            var edited = ToCapturedRequest(session, request);
            _syncedRequest = edited;

            if (!session.IsBypassingServer)
            {
                return new ExtensionRequestResult(edited);
            }

            var localResponse = ToCapturedResponse(session, template: null);
            _syncedResponse = localResponse;
            return new ExtensionRequestResult(edited, localResponse);
        }

        public void RequestAfter(CapturedRequest request)
        {
            var session = SyncRequest(request);
            _owner.RunEach(nameof(ShimAutoTamper.AutoTamperRequestAfter), tamper => tamper.AutoTamperRequestAfter(session));
        }

        public void PeekAtResponseHeaders(CapturedRequest request, CapturedResponse responsePreamble)
        {
            SyncRequest(request);
            var session = SyncResponse(responsePreamble);
            _owner.RunEach(nameof(ShimAutoTamper2.OnPeekAtResponseHeaders), tamper =>
            {
                if (tamper is ShimAutoTamper2 peeker)
                {
                    peeker.OnPeekAtResponseHeaders(session);
                }
            });

            _responsePeeked = responsePreamble.Headers;
            _responsePeekEdited = HeadersOf(session.oResponse.headers);
            _syncedResponse = null;
        }

        public CapturedResponse ResponseBefore(CapturedRequest request, CapturedResponse response)
        {
            SyncRequest(request);
            var session = SyncResponse(WithPeekEdits(response, ref _responsePeeked, ref _responsePeekEdited));
            _owner.RunEach(nameof(ShimAutoTamper.AutoTamperResponseBefore), tamper => tamper.AutoTamperResponseBefore(session));

            var edited = ToCapturedResponse(session, response);
            _syncedResponse = edited;
            return edited;
        }

        public void ResponseAfter(CapturedRequest request, CapturedResponse response)
        {
            SyncRequest(request);
            var session = SyncResponse(response);
            _owner.RunEach(nameof(ShimAutoTamper.AutoTamperResponseAfter), tamper => tamper.AutoTamperResponseAfter(session));
        }

        /// <summary>
        /// Creates the session on first use; afterwards replaces its request
        /// side only if the listener's request isn't the one this scope last
        /// saw. Flags always survive.
        /// </summary>
        internal ShimSession SyncRequest(CapturedRequest request)
        {
            if (_session is null)
            {
                _session = ShimSession.BuildFromData(
                    false, ToShimRequestHeaders(request), request.Body, new ShimResponseHeaders(), [], ShimSessionFlags.None);
                if (string.Equals(_scheme, "https", StringComparison.OrdinalIgnoreCase))
                {
                    _session.SetBitFlag(ShimSessionFlags.IsHTTPS, true);
                }
            }
            else if (!ReferenceEquals(request, _syncedRequest))
            {
                _session.oRequest.headers = ToShimRequestHeaders(request);
                _session.requestBodyBytes = request.Body;
            }

            _syncedRequest = request;
            return _session;
        }

        private ShimSession SyncResponse(CapturedResponse response)
        {
            var session = _session!;
            if (!ReferenceEquals(response, _syncedResponse))
            {
                session.oResponse.headers = ToShimResponseHeaders(response);
                session.responseBodyBytes = response.Body;
            }

            _syncedResponse = response;
            return session;
        }

        /// <summary>
        /// As in Fiddler, header changes an extension makes while peeking are
        /// kept. They're applied to the full message if its headers are still
        /// exactly the ones peeked at; if something else (FiddlerScript, a
        /// breakpoint) changed them in between, that change wins and the
        /// peek edits are dropped. Framing headers are safe to edit: the
        /// proxy recomputes Content-Length when it sends.
        /// </summary>
        private static CapturedRequest WithPeekEdits(
            CapturedRequest request,
            ref IReadOnlyList<(string Name, string Value)>? peeked,
            ref IReadOnlyList<(string Name, string Value)>? edited)
        {
            var result = request;
            if (peeked is not null && edited is not null && !peeked.SequenceEqual(edited) && request.Headers.SequenceEqual(peeked))
            {
                result = request with { Headers = edited };
            }

            peeked = null;
            edited = null;
            return result;
        }

        private static CapturedResponse WithPeekEdits(
            CapturedResponse response,
            ref IReadOnlyList<(string Name, string Value)>? peeked,
            ref IReadOnlyList<(string Name, string Value)>? edited)
        {
            var result = response;
            if (peeked is not null && edited is not null && !peeked.SequenceEqual(edited) && response.Headers.SequenceEqual(peeked))
            {
                result = response with { Headers = edited };
            }

            peeked = null;
            edited = null;
            return result;
        }

        private static IReadOnlyList<(string Name, string Value)> HeadersOf(Clearinet.CompatShim.HTTPHeaders headers) =>
            headers.Select(h => (h.Name, h.Value)).ToList();

        private ShimRequestHeaders ToShimRequestHeaders(CapturedRequest request)
        {
            var headers = new ShimRequestHeaders
            {
                HTTPMethod = request.Method,
                RequestPath = request.Target,
                HTTPVersion = request.HttpVersion,
                UriScheme = _scheme,
            };
            foreach (var (name, value) in request.Headers)
            {
                headers.Add(name, value);
            }

            // HTTP/1.1 requests carry Host; if one somehow doesn't, fall
            // back to the tunnel's host so hostname/fullUrl still work.
            if (!headers.Exists("Host"))
            {
                headers.Add("Host", _hostname);
            }

            return headers;
        }

        private static ShimResponseHeaders ToShimResponseHeaders(CapturedResponse response)
        {
            var headers = new ShimResponseHeaders { HTTPVersion = response.HttpVersion };
            headers.SetStatus(response.StatusCode, response.ReasonPhrase);
            foreach (var (name, value) in response.Headers)
            {
                headers.Add(name, value);
            }

            return headers;
        }

        private static CapturedRequest ToCapturedRequest(ShimSession session, CapturedRequest original)
        {
            var headers = session.oRequest.headers;
            return new CapturedRequest(
                headers.HTTPMethod,
                headers.RequestPath,
                string.IsNullOrEmpty(headers.HTTPVersion) ? original.HttpVersion : headers.HTTPVersion,
                headers.Select(h => (h.Name, h.Value)).ToList(),
                session.RequestBody);
        }

        private static CapturedResponse ToCapturedResponse(ShimSession session, CapturedResponse? template)
        {
            var headers = session.oResponse.headers;
            var version = string.IsNullOrEmpty(headers.HTTPVersion) ? template?.HttpVersion ?? "HTTP/1.1" : headers.HTTPVersion;
            return new CapturedResponse(
                version,
                headers.HTTPResponseCode,
                headers.StatusDescription,
                headers.Select(h => (h.Name, h.Value)).ToList(),
                session.ResponseBody);
        }
    }
}
