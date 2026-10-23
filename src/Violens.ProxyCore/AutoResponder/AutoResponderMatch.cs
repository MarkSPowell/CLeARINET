using System.Text.RegularExpressions;

namespace Violens.ProxyCore.AutoResponder;

/// <summary>
/// Parses an AutoResponder match pattern, following Fiddler Classic's
/// documented syntax:
/// <list type="bullet">
/// <item><description><c>METHOD:&lt;verb&gt; </c> prefix: only requests with that method.</description></item>
/// <item><description><c>NOT:&lt;pattern&gt;</c>: inverts the pattern.</description></item>
/// <item><description><c>EXACT:&lt;url&gt;</c>: case-sensitive match of the whole URL.</description></item>
/// <item><description><c>regex:&lt;pattern&gt;</c>: a .NET regex against the whole URL. Inline
/// options such as <c>(?insx)</c> work as-is.</description></item>
/// <item><description>Anything else: case-insensitive substring match, with <c>*</c> as a
/// wildcard.</description></item>
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
                // "METHOD:GET" alone matches any URL with that method.
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
            // A malformed regex throws, so a broken rule is noticed rather than
            // silently never matching.
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

    /// <summary>Escapes everything except <c>*</c>, which becomes <c>.*</c>.</summary>
    private static string WildcardToRegexPattern(string pattern)
    {
        var segments = pattern.Split('*');
        return string.Join(".*", segments.Select(Regex.Escape));
    }
}
