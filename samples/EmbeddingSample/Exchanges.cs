using System.Collections.Concurrent;
using System.Text;
using Violens.ProxyCore.Extensions;
using Violens.ProxyCore.Http;

namespace Contoso.WebDebugger;

/// <summary>
/// The app's own object for one request and its response -- the equivalent
/// of Fiddler's <c>Session</c> (or an <c>Exchange</c>) -- which lives from
/// the first hook to the last. ProxyCore hands each hook the current
/// request and response; the app keeps whatever else it wants here, such as
/// string flags (<c>ui-backcolor</c>, <c>ui-comments</c>), which end up in
/// the captured session's <c>Flags</c>.
/// </summary>
public sealed class Exchange
{
    public Exchange(int id, string host, string scheme)
    {
        Id = id;
        Host = host;
        Scheme = scheme;
    }

    public int Id { get; }
    public string Host { get; }
    public string Scheme { get; }
    public CapturedRequest? Request { get; internal set; }
    public CapturedResponse? Response { get; internal set; }

    /// <summary>Fiddler-style string flags. Case-insensitive names, as in Fiddler.</summary>
    public ConcurrentDictionary<string, string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string this[string flag]
    {
        get => Flags.TryGetValue(flag, out var value) ? value : string.Empty;
        set => Flags[flag] = value;
    }

    public string Url => $"{Scheme}://{Host}{(Request?.Target.StartsWith('/') == true ? Request.Target : string.Empty)}";
}

/// <summary>
/// The app's rules, in the shape of FiddlerScript's handlers. Each runs on a
/// connection's thread, never the UI thread, and must be quick: the request
/// or response waits for it.
/// </summary>
public interface IExchangeRules
{
    /// <summary>Like <c>OnBeforeRequest</c>. Return a response to answer the request without contacting the server.</summary>
    CapturedResponse? OnBeforeRequest(Exchange exchange, ref CapturedRequest request);

    /// <summary>Like <c>OnBeforeResponse</c>.</summary>
    void OnBeforeResponse(Exchange exchange, ref CapturedResponse response);
}

/// <summary>
/// Connects ProxyCore's per-request hooks to <see cref="Exchange"/> objects
/// and the app's <see cref="IExchangeRules"/>. ProxyCore calls
/// <see cref="BeginSession"/> once per request, then the
/// <see cref="IExtensionSession"/> methods in order.
/// </summary>
public sealed class ExchangeHost(IExchangeRules rules) : IExtensionSessionHost
{
    /// <summary>
    /// True: every request goes through the hooks. (When false, ProxyCore
    /// skips them and doesn't buffer bodies on their account, so a host that
    /// only sometimes has rules can return false the rest of the time.)
    /// </summary>
    public bool IsActive => true;

    /// <summary>Raised when an exchange starts, on the connection's thread.</summary>
    public event Action<Exchange>? ExchangeStarted;

    public IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme)
    {
        var exchange = new Exchange(sessionOrdinal, hostname, scheme);
        ExchangeStarted?.Invoke(exchange);
        return new Hooks(exchange, rules);
    }

    private sealed class Hooks(Exchange exchange, IExchangeRules rules) : IExtensionSession
    {
        public void PeekAtRequestHeaders(CapturedRequest requestPreamble) => exchange.Request = requestPreamble;

        // ProxyCore doesn't catch exceptions from these hooks (one would
        // drop the client's connection), so a failing rule is reported and
        // the message carries on unchanged.
        public ExtensionRequestResult RequestBefore(CapturedRequest request)
        {
            var original = request;
            try
            {
                var local = rules.OnBeforeRequest(exchange, ref request);
                exchange.Request = request;
                if (local is not null)
                {
                    exchange.Response = local;
                }

                return new ExtensionRequestResult(request, local);
            }
            catch (Exception ex)
            {
                exchange["ui-comments"] = $"OnBeforeRequest failed: {ex.Message}";
                exchange.Request = original;
                return new ExtensionRequestResult(original);
            }
        }

        public void RequestAfter(CapturedRequest request) => exchange.Request = request;

        public void PeekAtResponseHeaders(CapturedRequest request, CapturedResponse responsePreamble) =>
            exchange.Response = responsePreamble;

        public CapturedResponse ResponseBefore(CapturedRequest request, CapturedResponse response)
        {
            var original = response;
            try
            {
                rules.OnBeforeResponse(exchange, ref response);
            }
            catch (Exception ex)
            {
                exchange["ui-comments"] = $"OnBeforeResponse failed: {ex.Message}";
                response = original;
            }

            exchange.Response = response;
            return response;
        }

        public void ResponseAfter(CapturedRequest request, CapturedResponse response) => exchange.Response = response;

        public IReadOnlyDictionary<string, string>? Flags =>
            exchange.Flags.IsEmpty ? null : new Dictionary<string, string>(exchange.Flags, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Example rules, the kind a CustomRules.js would hold: tag every request,
/// answer one URL locally, and colour JSON responses in the session list.
/// </summary>
public sealed class SampleRules : IExchangeRules
{
    public CapturedResponse? OnBeforeRequest(Exchange exchange, ref CapturedRequest request)
    {
        // oSession.oRequest["X-Debugged-By"] = "Contoso";
        request = request with { Headers = [.. request.Headers, ("X-Debugged-By", "Contoso Web Debugger")] };

        // utilCreateResponseAndBypassServer(): answer without contacting the server.
        if (exchange.Host.Equals("example.com", StringComparison.OrdinalIgnoreCase) && request.Target.StartsWith("/hello", StringComparison.Ordinal))
        {
            exchange["ui-comments"] = "Answered locally by SampleRules";
            return TextResponse(200, "OK", "Hello from the proxy.\n");
        }

        return null;
    }

    public void OnBeforeResponse(Exchange exchange, ref CapturedResponse response)
    {
        var contentType = ContentDecoder.FindHeader(response.Headers, "Content-Type");
        if (ContentDecoder.MediaType(contentType) == "application/json")
        {
            exchange["ui-backcolor"] = "lightyellow";
        }
    }

    private static CapturedResponse TextResponse(int status, string reason, string text)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return new CapturedResponse(
            "HTTP/1.1",
            status,
            reason,
            [("Content-Type", "text/plain; charset=utf-8"), ("Content-Length", body.Length.ToString()), ("Cache-Control", "no-store")],
            body);
    }
}
