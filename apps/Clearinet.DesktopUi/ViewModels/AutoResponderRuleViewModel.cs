using Clearinet.ProxyCore.AutoResponder;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Thin bindable wrapper around one <see cref="AutoResponderRule"/> -- same
/// role <see cref="PendingBreakpointViewModel"/> plays for a
/// <c>PendingBreakpoint</c>, just simpler: an <see cref="AutoResponderRule"/>
/// is already plain mutable state (see that class's own remarks), so each
/// property here is a direct passthrough rather than a call into some
/// engine method. <see cref="Rule"/> is exposed so
/// <c>MainWindowViewModel</c> can reach the underlying model object when
/// adding/removing/reordering -- <see cref="AutoResponderRules.Rules"/> and
/// this view model's own <c>ObservableCollection</c> are two separate lists
/// kept in sync by hand, the same tradeoff <c>FilteredSessions</c> already
/// makes for the same reason (no Avalonia collection view that could just
/// project one from the other).
/// </summary>
public sealed class AutoResponderRuleViewModel(AutoResponderRule rule) : ViewModelBase
{
    public AutoResponderRule Rule { get; } = rule;

    public bool IsEnabled
    {
        get => Rule.IsEnabled;
        set
        {
            if (Rule.IsEnabled == value)
            {
                return;
            }

            Rule.IsEnabled = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>
    /// See <see cref="AutoResponderMatch"/> for the grammar this is parsed
    /// against at evaluation time -- there's nothing to validate here
    /// up front, since an unrecognized form just falls back to a plain
    /// literal/wildcard match rather than failing.
    /// </summary>
    public string MatchPattern
    {
        get => Rule.MatchPattern;
        set
        {
            if (Rule.MatchPattern == value)
            {
                return;
            }

            Rule.MatchPattern = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>See <see cref="AutoResponderAction"/> for the grammar this is parsed against.</summary>
    public string Action
    {
        get => Rule.Action;
        set
        {
            if (Rule.Action == value)
            {
                return;
            }

            Rule.Action = value;
            RaisePropertyChanged();
        }
    }
}
