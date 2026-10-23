namespace Violens.ProxyCore.AutoResponder;

/// <summary>
/// Parses an AutoResponder action in Fiddler Classic's syntax. Special forms
/// start with <c>*</c>; anything else is a URL to fetch or a file to serve.
/// </summary>
public sealed class AutoResponderAction
{
    public required AutoResponderActionKind Kind { get; init; }

    /// <summary>The file path, target URL, or redirect URL, depending on <see cref="Kind"/>. Null for every other kind.</summary>
    public string? Text { get; init; }

    /// <summary>For <see cref="AutoResponderActionKind.Delay"/>.</summary>
    public int Milliseconds { get; init; }

    /// <summary>The header or flag name, for <see cref="AutoResponderActionKind.SetHeader"/> and <see cref="AutoResponderActionKind.SetFlag"/>.</summary>
    public string? Name { get; init; }

    /// <summary>The header or flag value, for <see cref="AutoResponderActionKind.SetHeader"/> and <see cref="AutoResponderActionKind.SetFlag"/>.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// Whether this action ends rule evaluation. Delay, SetHeader and SetFlag
    /// don't, as in Fiddler.
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

        // Anything else is a local file to serve.
        return new AutoResponderAction { Kind = AutoResponderActionKind.ServeFile, Text = text };
    }

    /// <summary>Splits <c>Name=Value</c>; without <c>=</c> the whole text is the name, so half-typed rules don't throw.</summary>
    private static (string Name, string Value) SplitNameValue(string text)
    {
        var equalsIndex = text.IndexOf('=');
        return equalsIndex < 0 ? (text, string.Empty) : (text[..equalsIndex], text[(equalsIndex + 1)..]);
    }
}
