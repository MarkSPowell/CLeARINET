namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// Parses one <see cref="AutoResponderRule.Action"/> string into a
/// structured <see cref="AutoResponderActionKind"/> plus whatever data that
/// kind needs, following Fiddler Classic's own AutoResponder action syntax
/// (see <see cref="AutoResponderMatch"/>'s own remarks on the clean-room
/// policy this is built against). The special forms all start with
/// <c>*</c>; anything else is either a URL (fetch and serve it) or -- the
/// plain, common case -- a local file path to serve.
/// </summary>
public sealed class AutoResponderAction
{
    public required AutoResponderActionKind Kind { get; init; }

    /// <summary>The file path, target URL, or redirect URL, depending on <see cref="Kind"/>. Null for every other kind.</summary>
    public string? Text { get; init; }

    /// <summary>Only meaningful for <see cref="AutoResponderActionKind.Delay"/>.</summary>
    public int Milliseconds { get; init; }

    /// <summary>The header or flag name, for <see cref="AutoResponderActionKind.SetHeader"/>/<see cref="AutoResponderActionKind.SetFlag"/>.</summary>
    public string? Name { get; init; }

    /// <summary>The header or flag value, for <see cref="AutoResponderActionKind.SetHeader"/>/<see cref="AutoResponderActionKind.SetFlag"/>.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Whether this action stops rule evaluation outright. <see cref="AutoResponderActionKind.Delay"/>,
    /// <see cref="AutoResponderActionKind.SetHeader"/>, and
    /// <see cref="AutoResponderActionKind.SetFlag"/> are the only
    /// non-final kinds -- matching Fiddler's own "final vs. non-final
    /// actions" behavior (see the AutoResponder docs this was built
    /// against): everything else is exactly one rule's worth of "this is
    /// what happens to this request," full stop.
    /// </summary>
    public bool IsFinal => Kind is not (
        AutoResponderActionKind.Delay or AutoResponderActionKind.SetHeader or AutoResponderActionKind.SetFlag);

    public static AutoResponderAction Parse(string actionText)
    {
        var text = actionText.Trim();

        if (text.StartsWith("*delay:", StringComparison.OrdinalIgnoreCase))
        {
            var numberText = text["*delay:".Length..].TrimStart('+');
            var milliseconds = int.TryParse(numberText, out var parsed) ? Math.Max(0, parsed) : 0;
            return new AutoResponderAction { Kind = AutoResponderActionKind.Delay, Milliseconds = milliseconds };
        }

        if (text.StartsWith("*header:", StringComparison.OrdinalIgnoreCase))
        {
            var (name, value) = SplitNameValue(text["*header:".Length..]);
            return new AutoResponderAction { Kind = AutoResponderActionKind.SetHeader, Name = name, Value = value };
        }

        if (text.StartsWith("*flag:", StringComparison.OrdinalIgnoreCase))
        {
            var (name, value) = SplitNameValue(text["*flag:".Length..]);
            return new AutoResponderAction { Kind = AutoResponderActionKind.SetFlag, Name = name, Value = value };
        }

        if (text.StartsWith("*redir:", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.Redirect, Text = text["*redir:".Length..] };
        }

        if (string.Equals(text, "*bpu", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.BreakBeforeRequest };
        }

        if (string.Equals(text, "*bpafter", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.BreakAfterResponse };
        }

        if (string.Equals(text, "*reset", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.Reset };
        }

        if (string.Equals(text, "*drop", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.Drop };
        }

        if (string.Equals(text, "*CORSPreflightAllow", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.CorsPreflightAllow };
        }

        if (string.Equals(text, "*exit", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.Exit };
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new AutoResponderAction { Kind = AutoResponderActionKind.ProxyUrl, Text = text };
        }

        // Not one of the special forms and not a URL -- the plain, common
        // case: a local file path whose contents become the response body.
        return new AutoResponderAction { Kind = AutoResponderActionKind.ServeFile, Text = text };
    }

    /// <summary>
    /// Splits a <c>Name=Value</c> action payload. A missing <c>=</c> (a
    /// malformed or still-being-typed rule) treats the whole thing as the
    /// name with an empty value, rather than throwing -- the same
    /// "tolerant of an in-progress edit" stance
    /// <c>Clearinet.ProxyCore.Sessions.SessionQuery</c> already takes for a
    /// live filter box.
    /// </summary>
    private static (string Name, string Value) SplitNameValue(string text)
    {
        var equalsIndex = text.IndexOf('=');
        return equalsIndex < 0 ? (text, string.Empty) : (text[..equalsIndex], text[(equalsIndex + 1)..]);
    }
}
