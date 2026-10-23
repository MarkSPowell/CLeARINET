namespace Violens.ProxyCore.AutoResponder;

/// <summary>
/// One AutoResponder rule: a match pattern and an action, kept as the text
/// the user typed and parsed on each evaluation.
/// </summary>
public sealed class AutoResponderRule
{
    /// <summary>This rule's own checkbox, separate from the AutoResponder's on/off switch. On by default, as in Fiddler.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>See <see cref="AutoResponderMatch"/> for the syntax.</summary>
    public string MatchPattern { get; set; } = string.Empty;

    /// <summary>See <see cref="AutoResponderAction"/> for the syntax.</summary>
    public string Action { get; set; } = string.Empty;
}
