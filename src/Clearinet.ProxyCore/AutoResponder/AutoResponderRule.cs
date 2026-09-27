namespace Clearinet.ProxyCore.AutoResponder;

/// <summary>
/// One Fiddler-Classic-style AutoResponder rule: a match pattern (see
/// <see cref="AutoResponderMatch"/> for the grammar) paired with one action
/// (see <see cref="AutoResponderAction"/>). Deliberately just two plain
/// strings plus a toggle -- not a parsed/compiled form -- so a rule is
/// exactly what a text-editing UI round-trips: <see cref="AutoResponderRules.Evaluate"/>
/// parses <see cref="MatchPattern"/>/<see cref="Action"/> fresh on every
/// call, the same "cheap enough, and never stale" tradeoff
/// <c>Clearinet.ProxyCore.Sessions.SessionQuery.Parse</c> already makes for
/// a live filter box re-parsed on every keystroke.
///
/// Plain mutable state, not an <c>INotifyPropertyChanged</c> view model --
/// this lives in Clearinet.ProxyCore, which has no UI-framework opinions
/// (see <c>Clearinet.ProxyCore.Breakpoints.BreakpointRules</c>'s own
/// remarks for the identical reasoning). Clearinet.DesktopUi wraps
/// whichever properties it exposes as bindable ones.
/// </summary>
public sealed class AutoResponderRule
{
    /// <summary>
    /// Real Fiddler Classic gives every individual rule its own enabled
    /// checkbox, separate from AutoResponder's single global on/off switch
    /// (<see cref="AutoResponderRules.IsEnabled"/>) -- lets a rule be kept
    /// around, written and correct, without it actually firing. Defaults to
    /// <see langword="true"/> so a freshly added rule is live immediately,
    /// matching that same real-Fiddler default.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>See <see cref="AutoResponderMatch"/> for the full grammar this is parsed as.</summary>
    public string MatchPattern { get; set; } = string.Empty;

    /// <summary>See <see cref="AutoResponderAction"/> for the full grammar this is parsed as.</summary>
    public string Action { get; set; } = string.Empty;
}
