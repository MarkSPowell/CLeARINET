using Violens.ProxyCore.AutoResponder;

namespace Violens.DesktopUi.ViewModels;

/// <summary>Bindable wrapper around one <see cref="AutoResponderRule"/>.</summary>
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

    /// <summary>See <see cref="AutoResponderMatch"/>; anything unrecognised is a literal or wildcard match.</summary>
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
