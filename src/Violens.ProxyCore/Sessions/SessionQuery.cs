using System.Text;
using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Sessions;

/// <summary>
/// The session filter's query language, modelled on Chrome DevTools'
/// Network filter rather than QuickExec's sigils. Space-separated tokens are
/// ANDed; double quotes keep spaces in a value (<c>host:"my site"</c>).
/// <list type="bullet">
/// <item><description>plain text: case-insensitive substring of the URL or any header name
/// or value. Bodies aren't searched (that's what Find is for).</description></item>
/// <item><description><c>method:GET</c>, or <c>method:GET,POST</c> for either.</description></item>
/// <item><description><c>host:example.com</c>: substring of <see cref="Session.Host"/>.</description></item>
/// <item><description><c>status:200</c>, <c>status:4xx</c>, <c>status:&gt;=400</c> (also
/// <c>&gt;</c>, <c>&lt;</c>, <c>&lt;=</c>) or <c>status:400-499</c>. An unreadable value
/// matches nothing, so a typo shows no results rather than everything.</description></item>
/// </list>
/// An empty query matches every session.
/// </summary>
internal sealed class SessionQuery
{
    /// <summary>The empty query, which matches everything.</summary>
    public static readonly SessionQuery MatchAll = new([]);

    private readonly IReadOnlyList<Func<Session, bool>> _predicates;

    private SessionQuery(IReadOnlyList<Func<Session, bool>> predicates) => _predicates = predicates;

    /// <summary>Whether the query was empty.</summary>
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
    /// Splits on whitespace outside double quotes, which may start mid-token.
    /// An unclosed quote runs to the end, since the user may still be typing.
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

        // Not a known key:value token (a URL's "http:", say): match it as text.
        return FreeTextPredicate(token);
    }

    private static Func<Session, bool> FreeTextPredicate(string text) => session =>
        Url(session).Contains(text, StringComparison.OrdinalIgnoreCase)
        || HeadersContain(session.Request.Headers, text)
        || HeadersContain(session.Response.Headers, text);

    // Matches SessionRow.Url: the session's scheme, host and target.
    private static string Url(Session session) => $"{session.Scheme}://{session.Host}{session.Request.Target}";

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

        // Unreadable: match nothing.
        return _ => false;
    }
}
