using System.Text;
using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>
/// A small text query language for narrowing a session list -- closer to
/// Chrome DevTools' Network-panel filter box than to Fiddler Classic's
/// QuickExec (whose single-character sigils like <c>@host</c>/<c>=200</c>/
/// <c>&gt;1000</c> read as unfamiliar shorthand rather than something a
/// user already knows). This lives in <c>Clearinet.ProxyCore</c> rather
/// than the desktop app so every host application (tenet 4) gets the same
/// query behavior for free instead of reimplementing it against its own
/// session list.
///
/// Grammar: space-separated tokens, ANDed together. A double-quoted run
/// (anywhere inside a token, not just wrapping the whole thing) is taken
/// literally, including any whitespace inside it, so
/// <c>host:"my site" status:200</c> is two tokens, not three.
/// <list type="bullet">
/// <item><description>plain text -- substring match (case-insensitive)
/// against the request URL, or any request/response header's name or
/// value. Deliberately never searches body content: that can be large and
/// -- per <see cref="CapturedRequest"/>/<see cref="CapturedResponse"/>'s
/// Body -- is whatever encoding the wire actually used, not decoded text
/// (see <c>Clearinet.Extensibility.Inspection.Inspectors.RawTextInspector</c>
/// for where that decoding happens, on demand, for one selected session).</description></item>
/// <item><description><c>method:GET</c> -- exact match against the
/// request method; <c>method:GET,POST</c> matches either (OR within the
/// token, same as Chrome DevTools' comma-separated values).</description></item>
/// <item><description><c>host:example.com</c> -- substring match
/// (case-insensitive) against <see cref="Session.Host"/>.</description></item>
/// <item><description><c>status:200</c> (exact), <c>status:4xx</c> (status
/// class), <c>status:&gt;=400</c> / <c>status:&lt;500</c> / <c>status:&gt;400</c> /
/// <c>status:&lt;=499</c> (comparison), <c>status:400-499</c> (inclusive
/// range). A <c>status:</c> value that parses as none of these matches no
/// session -- a typo should read as "no results", not silently as "no
/// filter".</description></item>
/// </list>
/// An empty or whitespace-only query matches every session.
/// </summary>
public sealed class SessionQuery
{
    /// <summary>The parsed form of an empty query -- matches everything, and is what <see cref="Parse"/> returns for one.</summary>
    public static readonly SessionQuery MatchAll = new([]);

    private readonly IReadOnlyList<Func<Session, bool>> _predicates;

    private SessionQuery(IReadOnlyList<Func<Session, bool>> predicates) => _predicates = predicates;

    /// <summary>Whether this is <see cref="MatchAll"/> in substance (parsed from empty/whitespace text).</summary>
    public bool IsEmpty => _predicates.Count == 0;

    public bool Matches(Session session)
    {
        foreach (var predicate in _predicates)
        {
            if (!predicate(session))
            {
                return false;
            }
        }

        return true;
    }

    public static SessionQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return MatchAll;
        }

        var predicates = Tokenize(text).Select(ParseToken).ToArray();
        return new SessionQuery(predicates);
    }

    /// <summary>
    /// Splits on whitespace, except inside a double-quoted run -- which can
    /// start and end mid-token (<c>host:"my site"</c> is one token whose
    /// quotes are simply removed, not "host:\"my" split from "site\"").
    /// An unterminated quote runs to the end of the text rather than
    /// throwing; there's nothing a caller could usefully do to correct it
    /// mid-keystroke anyway, and a live filter box gets re-parsed on every
    /// change as the user keeps typing.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static Func<Session, bool> ParseToken(string token)
    {
        var colonIndex = token.IndexOf(':');
        if (colonIndex > 0 && colonIndex < token.Length - 1)
        {
            var key = token[..colonIndex];
            var value = token[(colonIndex + 1)..];

            if (string.Equals(key, "method", StringComparison.OrdinalIgnoreCase))
            {
                var methods = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (methods.Length > 0)
                {
                    return session => methods.Any(m => string.Equals(m, session.Request.Method, StringComparison.OrdinalIgnoreCase));
                }
            }
            else if (string.Equals(key, "host", StringComparison.OrdinalIgnoreCase))
            {
                return session => session.Host.Contains(value, StringComparison.OrdinalIgnoreCase);
            }
            else if (string.Equals(key, "status", StringComparison.OrdinalIgnoreCase))
            {
                return ParseStatusPredicate(value);
            }
        }

        // Not a recognized "key:value" token (no colon, colon at the very
        // start/end, or an unrecognized key like a URL's "http:") -- fall
        // back to matching the token as free text, whole, colon included.
        return FreeTextPredicate(token);
    }

    private static Func<Session, bool> FreeTextPredicate(string text) => session =>
        Url(session).Contains(text, StringComparison.OrdinalIgnoreCase)
        || HeadersContain(session.Request.Headers, text)
        || HeadersContain(session.Response.Headers, text);

    // Matches SessionRow.Url's own scheme assumption -- CapturedRequest
    // doesn't carry an explicit scheme (this proxy's whole design centers
    // on CONNECT-tunneled HTTPS interception), so this is cosmetic text for
    // substring matching, not a claim about what actually went on the wire.
    private static string Url(Session session) => $"https://{session.Host}{session.Request.Target}";

    private static bool HeadersContain(IReadOnlyList<(string Name, string Value)> headers, string text)
    {
        foreach (var (name, value) in headers)
        {
            if (name.Contains(text, StringComparison.OrdinalIgnoreCase) || value.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Func<Session, bool> ParseStatusPredicate(string value)
    {
        if (value.Length == 3
            && value[0] is >= '1' and <= '5'
            && (value[1] is 'x' or 'X')
            && (value[2] is 'x' or 'X'))
        {
            var statusClass = value[0] - '0';
            return session => session.Response.StatusCode / 100 == statusClass;
        }

        if (value.StartsWith(">=", StringComparison.Ordinal) && int.TryParse(value.AsSpan(2), out var gte))
        {
            return session => session.Response.StatusCode >= gte;
        }

        if (value.StartsWith("<=", StringComparison.Ordinal) && int.TryParse(value.AsSpan(2), out var lte))
        {
            return session => session.Response.StatusCode <= lte;
        }

        if (value.StartsWith(">", StringComparison.Ordinal) && int.TryParse(value.AsSpan(1), out var gt))
        {
            return session => session.Response.StatusCode > gt;
        }

        if (value.StartsWith("<", StringComparison.Ordinal) && int.TryParse(value.AsSpan(1), out var lt))
        {
            return session => session.Response.StatusCode < lt;
        }

        var dashIndex = value.IndexOf('-');
        if (dashIndex > 0
            && int.TryParse(value.AsSpan(0, dashIndex), out var low)
            && int.TryParse(value.AsSpan(dashIndex + 1), out var high))
        {
            return session => session.Response.StatusCode >= low && session.Response.StatusCode <= high;
        }

        if (int.TryParse(value, out var exact))
        {
            return session => session.Response.StatusCode == exact;
        }

        // Unparseable -- match nothing, so "status:abc" reads as "no
        // results, check your filter" instead of silently behaving like no
        // filter was applied at all.
        return _ => false;
    }
}
