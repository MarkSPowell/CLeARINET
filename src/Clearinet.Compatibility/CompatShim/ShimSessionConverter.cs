using System.Collections;
using Clearinet.Compatibility.Extensions;
using Clearinet.ProxyCore.Http;

namespace Clearinet.CompatShim;

/// <summary>
/// Turns a Fiddler-shaped <see cref="Session"/> into the
/// <see cref="ImportedSession"/> CLeARINET's own session list takes. What
/// carries over, and what doesn't yet:
///
/// <list type="bullet">
/// <item>Method, headers (order and repeats kept), bodies, status code,
/// reason phrase and HTTP versions: all of it.</item>
/// <item>URL: the host is <see cref="Session.host"/> (the Host header, or
/// the authority of an absolute-form request line), and the target is
/// always origin-form (<see cref="Session.PathAndQuery"/>), because the
/// session grid builds its URL column from host plus target.</item>
/// <item>Start time: the earliest request-side timestamp that was set
/// (<see cref="SessionTimers.ClientBeginRequest"/>, then
/// <see cref="SessionTimers.ClientConnected"/>, then
/// <see cref="SessionTimers.FiddlerBeginRequest"/>), or the import time if
/// none was. The other timers aren't kept yet: CLeARINET's native session
/// only has a start time.</item>
/// <item>String flags (<see cref="Session.oFlags"/>): all of them, keyed as
/// <see cref="System.Collections.Specialized.StringDictionary"/> reports
/// them (lower-cased, as in Fiddler's own SAZ files).</item>
/// <item><see cref="Session.BitFlags"/>: not kept yet.</item>
/// </list>
/// </summary>
public static class ShimSessionConverter
{
    public static ImportedSession ToImportedSession(Session session, DateTimeOffset importedAt)
    {
        ArgumentNullException.ThrowIfNull(session);

        var requestHeaders = session.oRequest.headers;
        var responseHeaders = session.oResponse.headers;

        var request = new CapturedRequest(
            requestHeaders.HTTPMethod,
            session.PathAndQuery,
            requestHeaders.HTTPVersion,
            requestHeaders.Select(h => (h.Name, h.Value)).ToList(),
            session.requestBodyBytes ?? Utilities.emptyByteArray);

        var response = new CapturedResponse(
            responseHeaders.HTTPVersion,
            responseHeaders.HTTPResponseCode,
            responseHeaders.StatusDescription,
            responseHeaders.Select(h => (h.Name, h.Value)).ToList(),
            session.responseBodyBytes ?? Utilities.emptyByteArray);

        var host = session.host;
        if (string.IsNullOrEmpty(host))
        {
            host = "(unknown host)";
        }

        return new ImportedSession(host, StartTime(session.Timers, importedAt), request, response, Flags(session));
    }

    private static DateTimeOffset StartTime(SessionTimers? timers, DateTimeOffset importedAt)
    {
        if (timers is null)
        {
            return importedAt;
        }

        foreach (var candidate in new[] { timers.ClientBeginRequest, timers.ClientConnected, timers.FiddlerBeginRequest })
        {
            if (candidate != default)
            {
                return new DateTimeOffset(candidate);
            }
        }

        return importedAt;
    }

    private static IReadOnlyDictionary<string, string>? Flags(Session session)
    {
        if (session.oFlags.Count == 0)
        {
            return null;
        }

        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in session.oFlags)
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                flags[name] = value;
            }
        }

        return flags;
    }
}
