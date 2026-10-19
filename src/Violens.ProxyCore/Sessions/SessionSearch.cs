using System.Text;
using System.Text.RegularExpressions;
using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Sessions;

/// <summary>Which side of each exchange Find looks at.</summary>
public enum SessionSearchTarget
{
    RequestsAndResponses,
    Requests,
    Responses,
}

/// <summary>Which parts of a message Find looks at.</summary>
public enum SessionSearchParts
{
    HeadersAndBodies,
    Headers,
    Bodies,
}

/// <summary>What to find, as set in the Find bar.</summary>
public sealed record SessionSearchOptions(
    string Text,
    SessionSearchTarget Target = SessionSearchTarget.RequestsAndResponses,
    SessionSearchParts Parts = SessionSearchParts.HeadersAndBodies,
    bool MatchCase = false,
    bool UseRegex = false);

/// <summary>
/// Fiddler Classic's Find Sessions (Ctrl+F): which sessions contain some
/// text. Unlike the filter box (see <see cref="SessionQuery"/>), which only
/// looks at the URL and headers so it can re-run on every keystroke, Find
/// also looks inside bodies, decompressed and read as text using their
/// charset, the way the Raw inspector shows them.
///
/// "Headers" includes the request line (method and full URL) or the
/// status line.
/// </summary>
public sealed class SessionSearch
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly SessionSearchOptions _options;
    private readonly Regex? _regex;
    private readonly StringComparison _comparison;

    private SessionSearch(SessionSearchOptions options, Regex? regex)
    {
        _options = options;
        _regex = regex;
        _comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
    }

    public SessionSearchOptions Options => _options;

    /// <summary>
    /// A search for <paramref name="options"/>, or false with a readable
    /// <paramref name="error"/> (nothing to find, or a regular expression
    /// that doesn't parse).
    /// </summary>
    public static bool TryCreate(SessionSearchOptions options, out SessionSearch? search, out string? error)
    {
        search = null;
        error = null;
        if (string.IsNullOrEmpty(options.Text))
        {
            error = "Type something to find.";
            return false;
        }

        Regex? regex = null;
        if (options.UseRegex)
        {
            try
            {
                var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Multiline;
                if (!options.MatchCase)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                regex = new Regex(options.Text, regexOptions, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                error = $"That isn't a valid regular expression: {ex.Message}";
                return false;
            }
        }

        search = new SessionSearch(options, regex);
        return true;
    }

    /// <summary>Whether <paramref name="session"/> contains what's being looked for.</summary>
    public bool Matches(Session session)
    {
        var target = _options.Target;
        if (target != SessionSearchTarget.Responses && MatchesRequest(session))
        {
            return true;
        }

        if (target != SessionSearchTarget.Requests && MatchesResponse(session.Response))
        {
            return true;
        }

        return MatchesWebSocket(session);
    }

    /// <summary>
    /// A WebSocket's text messages count as bodies: sent ones on the request
    /// side, received ones on the response side.
    /// </summary>
    private bool MatchesWebSocket(Session session)
    {
        if (session.WebSocket is not { } log || _options.Parts == SessionSearchParts.Headers)
        {
            return false;
        }

        foreach (var message in log.Messages)
        {
            if (message.Kind != WebSockets.WebSocketMessageKind.Text)
            {
                continue;
            }

            var side = message.Direction == WebSockets.WebSocketDirection.Sent
                ? SessionSearchTarget.Requests
                : SessionSearchTarget.Responses;
            if ((_options.Target == SessionSearchTarget.RequestsAndResponses || _options.Target == side) && IsMatch(message.Text))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesRequest(Session session)
    {
        var request = session.Request;
        if (_options.Parts != SessionSearchParts.Bodies &&
            IsMatch(HeaderText($"{request.Method} {SessionUrl.Of(session)} {request.HttpVersion}", request.Headers)))
        {
            return true;
        }

        return _options.Parts != SessionSearchParts.Headers && MatchesBody(request.Body, request.Headers);
    }

    private bool MatchesResponse(CapturedResponse response)
    {
        if (_options.Parts != SessionSearchParts.Bodies &&
            IsMatch(HeaderText($"{response.HttpVersion} {response.StatusCode} {response.ReasonPhrase}", response.Headers)))
        {
            return true;
        }

        return _options.Parts != SessionSearchParts.Headers && MatchesBody(response.Body, response.Headers);
    }

    private bool MatchesBody(byte[] body, IReadOnlyList<(string Name, string Value)> headers)
    {
        if (body.Length == 0)
        {
            return false;
        }

        // A body that can't be decompressed is searched as it is.
        ContentDecoder.TryDecode(body, headers, out var decoded, out _);
        return IsMatch(ContentDecoder.DecodeText(decoded, ContentDecoder.FindHeader(headers, "Content-Type")));
    }

    private static string HeaderText(string startLine, IReadOnlyList<(string Name, string Value)> headers)
    {
        var text = new StringBuilder(startLine).Append("\r\n");
        foreach (var (name, value) in headers)
        {
            text.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        return text.ToString();
    }

    private bool IsMatch(string text)
    {
        if (_regex is null)
        {
            return text.Contains(_options.Text, _comparison);
        }

        try
        {
            return _regex.IsMatch(text);
        }
        catch (RegexMatchTimeoutException)
        {
            return false; // a pathological pattern on a big body: treat as no match rather than hang
        }
    }
}
