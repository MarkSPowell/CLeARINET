using System;
using System.Collections.Generic;
using System.Linq;
using Violens.CompatShim;
using Violens.LegacyExtensionHost.Bridge;

namespace Violens.LegacyExtensionHost;

/// <summary>Converts between the bridge's wire types and the shim's <see cref="Session"/>.</summary>
internal static class SessionMapping
{
    /// <summary>For the request hooks: there's no response yet.</summary>
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
        // The wire request this bridge receives doesn't carry the scheme,
        // so url/fullUrl/UriScheme below assume HTTPS. VioLens's listener
        // now captures plain http:// too; carry Session.Scheme across if
        // this bridge ever needs to tell the two apart.
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
    /// The shim's header indexer replaces a same-named header rather than
    /// adding another, so for repeated headers (<c>Set-Cookie</c>) only the
    /// last survives. Logged when it happens.
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
    /// The session's request after the hooks ran. <paramref name="original"/>
    /// supplies the HTTP version, which <see cref="Session"/> doesn't have.
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
    /// The session's response after the hooks ran. <paramref name="original"/>
    /// supplies the reason phrase if the status line has none.
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
