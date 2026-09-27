using System.Text.RegularExpressions;

namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// Parses one <see cref="AutoResponderRule.MatchPattern"/> into a compiled
/// predicate over a request's method and full URL, following Fiddler
/// Classic's own AutoResponder match syntax (per its published
/// documentation -- see <c>docs/CLeARINET Project Plan and Goals.md</c>'s
/// clean-room policy for why this is built against that, never decompiled
/// or leaked source):
/// <list type="bullet">
/// <item><description>An optional leading <c>METHOD:&lt;verb&gt; </c>
/// (verb, then a space) restricts the rest of the pattern to requests using
/// that HTTP method.</description></item>
/// <item><description><c>NOT:&lt;pattern&gt;</c> inverts whatever
/// <c>&lt;pattern&gt;</c> would otherwise match.</description></item>
/// <item><description><c>EXACT:&lt;url&gt;</c> requires a complete,
/// case-sensitive match against the full URL -- no substring, no
/// wildcard.</description></item>
/// <item><description><c>regex:&lt;pattern&gt;</c> is a plain .NET
/// <see cref="Regex"/> tested against the full URL. Fiddler's own docs show
/// inline modifier groups like <c>(?insx)</c> at the start of a pattern --
/// .NET's own regex engine already parses those natively (<c>i</c> =
/// IgnoreCase, <c>n</c> = ExplicitCapture, <c>s</c> = Singleline, <c>x</c> =
/// IgnorePatternWhitespace), so nothing extra is needed here to support
/// them.</description></item>
/// <item><description>Anything else is a case-insensitive literal match:
/// a bare <c>*</c> inside it is a wildcard (translated to <c>.*</c>);
/// everything else in it is matched literally, including any other regex
/// metacharacters it happens to contain. With no <c>*</c> at all this is
/// just a case-insensitive substring (<c>Contains</c>) match, the common
/// case in practice.</description></item>
/// </list>
/// </summary>
public sealed class AutoResponderMatch
{
    private readonly string? _method;
    private readonly bool _invert;
    private readonly Func<string, bool> _innerMatches;

    private AutoResponderMatch(string? method, bool invert, Func<string, bool> innerMatches)
    {
        _method = method;
        _invert = invert;
        _innerMatches = innerMatches;
    }

    public static AutoResponderMatch Parse(string pattern)
    {
        var remainder = pattern.Trim();

        string? method = null;
        if (remainder.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase))
        {
            var afterKeyword = remainder["METHOD:".Length..];
            var spaceIndex = afterKeyword.IndexOf(' ');
            if (spaceIndex >= 0)
            {
                method = afterKeyword[..spaceIndex];
                remainder = afterKeyword[(spaceIndex + 1)..].TrimStart();
            }
            else
            {
                // "METHOD:GET" with nothing after it -- treat the verb
                // itself as the whole restriction and match any URL, rather
                // than throwing on what's obviously a still-being-typed
                // rule in a live editor.
                method = afterKeyword;
                remainder = string.Empty;
            }
        }

        var invert = false;
        if (remainder.StartsWith("NOT:", StringComparison.OrdinalIgnoreCase))
        {
            invert = true;
            remainder = remainder["NOT:".Length..];
        }

        var innerMatches = ParseInner(remainder);
        return new AutoResponderMatch(method, invert, innerMatches);
    }

    public bool Matches(string method, string url)
    {
        if (_method is not null && !string.Equals(_method, method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var matched = _innerMatches(url);
        return _invert ? !matched : matched;
    }

    private static Func<string, bool> ParseInner(string pattern)
    {
        if (pattern.StartsWith("EXACT:", StringComparison.OrdinalIgnoreCase))
        {
            var exact = pattern["EXACT:".Length..];
            return url => string.Equals(url, exact, StringComparison.Ordinal);
        }

        if (pattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            // Constructed once per Parse call, not once per rule for the
            // process's whole life -- see the class remarks on why
            // re-parsing per evaluation is an accepted tradeoff here. A
            // malformed pattern throws ArgumentException from here, which
            // AutoResponderRules.Evaluate lets propagate rather than
            // silently treating as "never matches" -- a broken regex rule
            // should be loud, not quietly inert.
            var regex = new Regex(pattern["regex:".Length..]);
            return url => regex.IsMatch(url);
        }

        if (pattern.Contains('*'))
        {
            var regex = new Regex(WildcardToRegexPattern(pattern), RegexOptions.IgnoreCase);
            return url => regex.IsMatch(url);
        }

        return url => url.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Escapes every regex metacharacter in <paramref name="pattern"/>
    /// except <c>*</c>, then turns each <c>*</c> into <c>.*</c> -- so a
    /// pattern like <c>api.example.com/*</c> matches that whole prefix
    /// without its literal <c>.</c>s accidentally becoming "any character"
    /// wildcards of their own.
    /// </summary>
    private static string WildcardToRegexPattern(string pattern)
    {
        var segments = pattern.Split('*');
        return string.Join(".*", segments.Select(Regex.Escape));
    }
}
