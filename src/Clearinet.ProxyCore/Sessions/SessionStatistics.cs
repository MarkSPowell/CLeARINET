using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// What kind of resource a response is, by its Content-Type, using the
/// groups browsers' developer tools filter by.
/// </summary>
public enum ResourceKind
{
    Document,
    Script,
    Stylesheet,
    Image,
    Font,
    Data,
    Media,
    Other,
}

/// <summary>One group in a breakdown: how many sessions, how many bytes received, and the total time.</summary>
public sealed record StatisticsGroup(string Name, int Count, long ResponseBytes, double TotalMilliseconds);

/// <summary>
/// The Statistics view, for a set of sessions: totals, breakdowns by status,
/// resource kind and host, the slowest requests, and easy wins (text sent
/// uncompressed, responses with no caching headers). Fiddler Classic's
/// Statistics tab, laid out the way browsers' developer tools summarise
/// a page load.
/// </summary>
public sealed record SessionStatistics
{
    // Text responses smaller than this aren't worth compressing.
    private const int CompressibleThreshold = 1024;

    public int Requests { get; init; }

    /// <summary>From the first request starting to the last response finishing.</summary>
    public TimeSpan Span { get; init; }

    public long RequestBytes { get; init; }

    /// <summary>Response bodies as they arrived (compressed, if they were).</summary>
    public long ResponseBytes { get; init; }

    /// <summary>Response bodies once decompressed.</summary>
    public long DecodedResponseBytes { get; init; }

    public IReadOnlyList<StatisticsGroup> ByStatus { get; init; } = [];

    public IReadOnlyList<StatisticsGroup> ByKind { get; init; } = [];

    /// <summary>The busiest hosts, by number of requests (at most ten).</summary>
    public IReadOnlyList<StatisticsGroup> ByHost { get; init; } = [];

    /// <summary>The slowest sessions with timings (at most five), slowest first.</summary>
    public IReadOnlyList<Session> Slowest { get; init; } = [];

    /// <summary>Text responses of 1 KB or more sent without compression.</summary>
    public IReadOnlyList<Session> Uncompressed { get; init; } = [];

    /// <summary>Successful responses to GET with no Cache-Control, Expires, ETag or Last-Modified.</summary>
    public IReadOnlyList<Session> NotCacheable { get; init; } = [];

    public static SessionStatistics Compute(IReadOnlyCollection<Session> sessions)
    {
        if (sessions.Count == 0)
        {
            return new SessionStatistics();
        }

        var first = sessions.Min(s => s.StartedAt);
        var last = sessions.Max(End);

        var decoded = 0L;
        var uncompressed = new List<Session>();
        var notCacheable = new List<Session>();
        foreach (var session in sessions)
        {
            var response = session.Response;
            var encoding = ContentDecoder.FindHeader(response.Headers, "Content-Encoding");
            decoded += ContentDecoder.TryDecode(response.Body, encoding, out var body, out _) ? body.LongLength : response.Body.LongLength;

            if (string.IsNullOrWhiteSpace(encoding) && response.Body.Length >= CompressibleThreshold && IsText(response))
            {
                uncompressed.Add(session);
            }

            if (string.Equals(session.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                response.StatusCode == 200 && !HasCachingHeaders(response))
            {
                notCacheable.Add(session);
            }
        }

        return new SessionStatistics
        {
            Requests = sessions.Count,
            Span = last > first ? last - first : TimeSpan.Zero,
            RequestBytes = sessions.Sum(s => (long)s.Request.Body.Length),
            ResponseBytes = sessions.Sum(s => (long)s.Response.Body.Length),
            DecodedResponseBytes = decoded,
            ByStatus = Group(sessions, s => StatusClass(s.Response.StatusCode)).OrderBy(g => g.Name).ToList(),
            ByKind = Group(sessions, s => KindOf(s.Response).ToString()).OrderByDescending(g => g.ResponseBytes).ToList(),
            ByHost = Group(sessions, s => s.Host).OrderByDescending(g => g.Count).ThenBy(g => g.Name).Take(10).ToList(),
            Slowest = sessions.Where(s => s.Timings is not null).OrderByDescending(s => s.Timings!.Total).Take(5).ToList(),
            Uncompressed = uncompressed.OrderByDescending(s => s.Response.Body.Length).ToList(),
            NotCacheable = notCacheable,
        };
    }

    /// <summary>The kind of resource, from the response's Content-Type.</summary>
    public static ResourceKind KindOf(CapturedResponse response)
    {
        var type = ContentDecoder.MediaType(ContentDecoder.FindHeader(response.Headers, "Content-Type"));
        return type switch
        {
            "text/html" or "application/xhtml+xml" => ResourceKind.Document,
            "text/javascript" or "application/javascript" or "application/x-javascript" or "application/ecmascript" or "text/ecmascript" => ResourceKind.Script,
            "text/css" => ResourceKind.Stylesheet,
            "application/json" or "text/json" or "application/xml" or "text/xml" or "text/plain" or "application/x-www-form-urlencoded" => ResourceKind.Data,
            _ when type.StartsWith("image/", StringComparison.Ordinal) => ResourceKind.Image,
            _ when type.StartsWith("font/", StringComparison.Ordinal) || type is "application/font-woff" or "application/x-font-woff" or "application/vnd.ms-fontobject" => ResourceKind.Font,
            _ when type.StartsWith("audio/", StringComparison.Ordinal) || type.StartsWith("video/", StringComparison.Ordinal) => ResourceKind.Media,
            _ when type.EndsWith("+json", StringComparison.Ordinal) || type.EndsWith("+xml", StringComparison.Ordinal) => ResourceKind.Data,
            _ => ResourceKind.Other,
        };
    }

    /// <summary>When the session's response finished (its start when timings aren't known).</summary>
    public static DateTimeOffset End(Session session) =>
        session.StartedAt + TimeSpan.FromMilliseconds(session.Timings?.Total ?? 0);

    private static string StatusClass(int status) => status switch
    {
        >= 100 and < 600 => $"{status / 100}xx",
        _ => "No response",
    };

    private static List<StatisticsGroup> Group(IEnumerable<Session> sessions, Func<Session, string> key) =>
        sessions
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StatisticsGroup(
                g.Key,
                g.Count(),
                g.Sum(s => (long)s.Response.Body.Length),
                g.Sum(s => s.Timings?.Total ?? 0)))
            .ToList();

    private static bool IsText(CapturedResponse response) =>
        KindOf(response) is ResourceKind.Document or ResourceKind.Script or ResourceKind.Stylesheet or ResourceKind.Data ||
        ContentDecoder.MediaType(ContentDecoder.FindHeader(response.Headers, "Content-Type")) == "image/svg+xml";

    private static bool HasCachingHeaders(CapturedResponse response) =>
        new[] { "Cache-Control", "Expires", "ETag", "Last-Modified" }
            .Any(name => ContentDecoder.FindHeader(response.Headers, name) is not null);
}
