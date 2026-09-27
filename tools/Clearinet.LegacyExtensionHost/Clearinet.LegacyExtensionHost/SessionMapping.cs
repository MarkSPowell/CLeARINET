using System;
using System.Collections.Generic;
using System.Linq;
using Clearinet.CompatShim;
using Clearinet.LegacyExtensionHost.Bridge;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// Converts between the session bridge's wire DTOs
/// (<c>Clearinet.LegacyExtensionHost.Bridge.WireRequest</c>/<c>WireResponse</c>)
/// and this project's own <see cref="Session"/> -- the one place that
/// mapping happens, used by <see cref="SessionBridgeRunner"/> for every
/// hook kind. Kept as a separate static class (rather than folded directly
/// into the runner) specifically so it's independently testable: building a
/// correct <see cref="Session"/> from real captured traffic, and correctly
/// reading extension edits back out of one, is exactly the part of this
/// bridge most worth a unit test, with no pipe or process involved.
/// </summary>
internal static class SessionMapping
{
    /// <summary>
    /// Builds a <see cref="Session"/> for <c>AutoTamperRequestBefore</c>/
    /// <c>AutoTamperRequestAfter</c> -- no response exists yet at that point
    /// (matching real Fiddler's own timing, and
    /// <c>Clearinet.Compatibility.FiddlerScript.Exchange.ForRequest</c>'s
    /// identical "no response yet" shape on the in-process side of this same
    /// distinction).
    /// </summary>
    public static Session ToRequestSession(int id, string hostname, WireRequest request, Action<string> log) =>
        Populate(new Session(), id, hostname, request, log);

    /// <summary>
    /// Builds a <see cref="Session"/> for <c>AutoTamperResponseBefore</c>/
    /// <c>AutoTamperResponseAfter</c>, once a response exists too.
    /// </summary>
    public static Session ToResponseSession(int id, string hostname, WireRequest request, WireResponse response, Action<string> log)
    {
        var session = Populate(new Session(), id, hostname, request, log);

        session.responseCode = response.StatusCode;
        session.oResponse.headers.HTTPResponseStatus = string.IsNullOrEmpty(response.ReasonPhrase)
            ? response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{response.StatusCode} {response.ReasonPhrase}";
        SetHeaders(session.oResponse.headers, response.Headers, "response", log);
        session.responseBodyBytes = response.Body ?? Array.Empty<byte>();

        return session;
    }

    private static Session Populate(Session session, int id, string hostname, WireRequest request, Action<string> log)
    {
        session.id = id;
        session.host = hostname;
        session.PathAndQuery = request.Target;
        // CLeARINET's own InterceptingProxyListener only ever intercepts
        // HTTPS tunnels today (see that class's own remarks: "this spike
        // only handles HTTPS tunnels") -- url/fullUrl/UriScheme below all
        // assume that scope. Revisit if/when plain-HTTP proxying is added
        // and this bridge starts seeing requests that came in that way too.
        session.url = $"{hostname}{request.Target}";
        session.fullUrl = $"https://{hostname}{request.Target}";
        session.state = SessionStates.Done;

        session.oRequest.headers.HTTPMethod = request.Method;
        session.oRequest.headers.UriScheme = "https";
        session.oRequest.headers.RequestPath = SplitPath(request.Target);
        SetHeaders(session.oRequest.headers, request.Headers, "request", log);
        session.requestBodyBytes = request.Body ?? Array.Empty<byte>();

        return session;
    }

    /// <summary>
    /// <see cref="HTTPHeaders"/>'s own indexer setter (see that class's own
    /// remarks) replaces an existing same-named header rather than adding a
    /// second one -- a real gap for anything that legitimately repeats a
    /// header name (multiple <c>Set-Cookie</c> headers being the common
    /// real-world case), inherited from how this shim's header store is
    /// built, not introduced here. Only the last value for a repeated name
    /// survives into the <see cref="Session"/> a loaded extension actually
    /// sees -- flagged with a log line so this is visible on the Log tab
    /// rather than a silent data loss, not solved by this bridge (fixing
    /// <see cref="HTTPHeaders"/> itself to support repeated names is future
    /// work, and would need checking it doesn't change any of the five real
    /// samples' own confirmed behavior against it).
    /// </summary>
    private static void SetHeaders(HTTPHeaders headers, IReadOnlyList<WireHeader> wireHeaders, string which, Action<string> log)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in wireHeaders)
        {
            if (!seen.Add(header.Name))
            {
                log($"[SessionBridge] Repeated {which} header \"{header.Name}\" -- only the last value is visible to loaded extensions (see SessionMapping's own remarks).");
            }

            headers[header.Name] = header.Value;
        }
    }

    private static string SplitPath(string target)
    {
        var queryIndex = target.IndexOf('?');
        return queryIndex >= 0 ? target.Substring(0, queryIndex) : target;
    }

    /// <summary>
    /// Projects a <see cref="Session"/>'s current request state back into a
    /// <see cref="WireRequest"/>, after every loaded extension's
    /// <c>AutoTamperRequestBefore</c>/<c>AutoTamperRequestAfter</c> has run.
    /// <paramref name="original"/> supplies <c>HttpVersion</c>, which
    /// <see cref="Session"/> has no member for at all (none of the five real
    /// extensions this shim was built against reference one -- see
    /// <c>ClientChatter</c>'s own remarks) and which no loaded extension can
    /// therefore have changed.
    /// </summary>
    public static WireRequest FromSession(Session session, WireRequest original) => new()
    {
        Method = session.oRequest.headers.HTTPMethod ?? original.Method,
        Target = session.PathAndQuery ?? original.Target,
        HttpVersion = original.HttpVersion,
        Headers = session.oRequest.headers.AllHeaders.Select(h => new WireHeader { Name = h.Key, Value = h.Value }).ToList(),
        Body = session.requestBodyBytes ?? original.Body,
    };

    /// <summary>
    /// Same as <see cref="FromSession(Session, WireRequest)"/>, for the
    /// response side. <paramref name="original"/> also backstops
    /// <c>ReasonPhrase</c> when <c>oResponse.headers.HTTPResponseStatus</c>
    /// wasn't left in a "code reason" shape an extension could have set it
    /// to anything, or left it as this bridge seeded it in
    /// <see cref="ToResponseSession"/> -- both parse the same way here, so
    /// this only actually falls back to <paramref name="original"/> when
    /// that string is empty or code-only.
    /// </summary>
    public static WireResponse FromSession(Session session, WireResponse original) => new()
    {
        HttpVersion = original.HttpVersion,
        StatusCode = session.responseCode,
        ReasonPhrase = ExtractReasonPhrase(session.oResponse.headers.HTTPResponseStatus) ?? original.ReasonPhrase,
        Headers = session.oResponse.headers.AllHeaders.Select(h => new WireHeader { Name = h.Key, Value = h.Value }).ToList(),
        Body = session.responseBodyBytes ?? original.Body,
    };

    private static string ExtractReasonPhrase(string httpResponseStatus)
    {
        if (string.IsNullOrEmpty(httpResponseStatus))
        {
            return null;
        }

        var spaceIndex = httpResponseStatus.IndexOf(' ');
        return spaceIndex >= 0 && spaceIndex + 1 < httpResponseStatus.Length
            ? httpResponseStatus.Substring(spaceIndex + 1)
            : null;
    }
}
