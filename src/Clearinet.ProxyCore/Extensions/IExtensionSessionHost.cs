using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Extensions;

/// <summary>
/// Runs Fiddler-shaped extension hooks (<c>OnPeekAtRequestHeaders</c>,
/// <c>AutoTamperRequestBefore</c> and the rest) against live traffic, with
/// <b>one session object per request</b> that lives from the first hook to
/// the last. That's what ported Fiddler Classic extensions expect: flags an
/// extension sets in one hook are still there in the next, and a response it
/// creates in <c>AutoTamperRequestBefore</c> is sent instead of contacting
/// the server.
///
/// It sits beside <see cref="IExtensionAutoTamperHost"/>, which it doesn't
/// replace. That older contract is stateless (a fresh object per hook) and
/// is still what CLeARINET-native extensions and the legacy host's session
/// bridge use.
///
/// Lives in ProxyCore for the same reason <see cref="IExtensionAutoTamperHost"/>
/// does: the listener can't reference <c>Clearinet.Compatibility</c>, so
/// the composition root hands it the real implementation
/// (<c>Clearinet.Compatibility.Extensions.ShimAutoTamperSet</c>) through
/// this interface.
/// </summary>
public interface IExtensionSessionHost
{
    /// <summary>
    /// Whether any extension is listening. When false, the listener never
    /// calls <see cref="BeginSession"/>, so traffic isn't buffered on these
    /// hooks' account.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Starts one request's worth of hooks. Called once per request, after
    /// its headers have arrived and before any hook runs.
    /// </summary>
    /// <param name="sessionOrdinal">The id the session will most likely get in the session list.</param>
    /// <param name="hostname">The host from the CONNECT tunnel the request arrived on.</param>
    /// <param name="scheme">The request's scheme, e.g. <c>https</c>.</param>
    IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme);
}

/// <summary>
/// One request's hooks, in the order the listener calls them. Every method
/// is safe to call even if no extension implements that hook, and none of
/// them throws: an extension that throws is logged and skipped, and the
/// others still run.
///
/// The request and response passed in each time are the listener's current
/// ones, which FiddlerScript or a breakpoint may have changed since the
/// previous hook. An implementation keeps its own session object and only
/// brings it up to date with those changes.
/// </summary>
public interface IExtensionSession
{
    /// <summary>
    /// <c>OnPeekAtRequestHeaders</c>: the request line and headers only,
    /// before the body is read. Header changes made here are applied in
    /// <see cref="RequestBefore"/>, unless something else changed the headers
    /// in between.
    /// </summary>
    void PeekAtRequestHeaders(CapturedRequest requestPreamble);

    /// <summary>
    /// <c>AutoTamperRequestBefore</c>: the whole request, before it's sent.
    /// The result carries any edits. When an extension answered the request
    /// itself (Fiddler's <c>utilCreateResponseAndBypassServer</c>),
    /// <see cref="ExtensionRequestResult.LocalResponse"/> is that answer and
    /// the request must not be sent to the server.
    /// </summary>
    ExtensionRequestResult RequestBefore(CapturedRequest request);

    /// <summary><c>AutoTamperRequestAfter</c>: the request as sent. Changes made here aren't sent.</summary>
    void RequestAfter(CapturedRequest request);

    /// <summary>
    /// <c>OnPeekAtResponseHeaders</c>: the status line and headers only,
    /// before the body is read. Header changes made here are applied in
    /// <see cref="ResponseBefore"/>, unless something else changed the headers
    /// in between.
    /// </summary>
    void PeekAtResponseHeaders(CapturedRequest request, CapturedResponse responsePreamble);

    /// <summary><c>AutoTamperResponseBefore</c>: the whole response, before it's returned to the client. Returns it with any edits.</summary>
    CapturedResponse ResponseBefore(CapturedRequest request, CapturedResponse response);

    /// <summary><c>AutoTamperResponseAfter</c>: the response as returned. Changes made here aren't sent.</summary>
    void ResponseAfter(CapturedRequest request, CapturedResponse response);

    /// <summary>
    /// The session's string flags (<c>ui-backcolor</c>, <c>ui-comments</c>,
    /// ...) as the extensions left them, or null if there are none. Recorded
    /// with the session, so they show on its Notes tab.
    /// </summary>
    IReadOnlyDictionary<string, string>? Flags { get; }
}

/// <summary>What <see cref="IExtensionSession.RequestBefore"/> produced.</summary>
/// <param name="Request">The request, with any edits.</param>
/// <param name="LocalResponse">A response an extension created instead of contacting the server, or null to send the request as usual.</param>
public sealed record ExtensionRequestResult(CapturedRequest Request, CapturedResponse? LocalResponse = null);
