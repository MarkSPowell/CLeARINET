using Avalonia.Threading;
using Clearinet.DesktopUi.Models;
using Clearinet.ProxyCore.Sessions;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Edit &gt; Find Sessions (Ctrl+F), Fiddler Classic's Find: highlights every
/// captured session that contains some text, in its URL, headers or
/// (decompressed) bodies, and selects the first one. F3 moves to the next.
/// Unlike the filter box, it doesn't hide anything. See <see cref="SessionSearch"/>.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private bool _isFindBarVisible;
    private string _findText = string.Empty;
    private int _findTargetIndex;
    private int _findPartsIndex;
    private bool _findMatchCase;
    private bool _findUseRegex;
    private string _findStatus = string.Empty;
    private bool _isFinding;
    private RelayCommand? _showFindCommand;
    private RelayCommand? _findCommand;
    private RelayCommand? _findNextCommand;
    private RelayCommand? _closeFindCommand;

    /// <summary>Raised when the Find bar should take keyboard focus.</summary>
    public event Action? FindFocusRequested;

    /// <summary>Raised when which rows are highlighted changed, so the session list redraws them.</summary>
    public event Action? FindResultsChanged;

    public bool IsFindBarVisible
    {
        get => _isFindBarVisible;
        private set => SetField(ref _isFindBarVisible, value);
    }

    public string FindText
    {
        get => _findText;
        set => SetField(ref _findText, value ?? string.Empty);
    }

    /// <summary>0: requests and responses, 1: requests only, 2: responses only.</summary>
    public int FindTargetIndex
    {
        get => _findTargetIndex;
        set => SetField(ref _findTargetIndex, value);
    }

    /// <summary>0: headers and bodies, 1: headers only, 2: bodies only.</summary>
    public int FindPartsIndex
    {
        get => _findPartsIndex;
        set => SetField(ref _findPartsIndex, value);
    }

    public bool FindMatchCase
    {
        get => _findMatchCase;
        set => SetField(ref _findMatchCase, value);
    }

    public bool FindUseRegex
    {
        get => _findUseRegex;
        set => SetField(ref _findUseRegex, value);
    }

    /// <summary>"3 sessions match", or why nothing could be searched for.</summary>
    public string FindStatus
    {
        get => _findStatus;
        private set => SetField(ref _findStatus, value);
    }

    public RelayCommand ShowFindCommand => _showFindCommand ??= new RelayCommand(() =>
    {
        IsFindBarVisible = true;
        FindFocusRequested?.Invoke();
    });

    public RelayCommand FindCommand => _findCommand ??= new RelayCommand(() => _ = FindAsync());

    public RelayCommand FindNextCommand => _findNextCommand ??= new RelayCommand(FindNext);

    public RelayCommand CloseFindCommand => _closeFindCommand ??= new RelayCommand(CloseFind);

    /// <summary>
    /// Searches every captured session (in the background: bodies can be
    /// large), highlights the matches and selects the first one that the
    /// filter isn't hiding.
    /// </summary>
    private async Task FindAsync()
    {
        IsFindBarVisible = true;
        if (_isFinding)
        {
            return;
        }

        var options = new SessionSearchOptions(
            FindText,
            (SessionSearchTarget)Math.Clamp(FindTargetIndex, 0, 2),
            (SessionSearchParts)Math.Clamp(FindPartsIndex, 0, 2),
            FindMatchCase,
            FindUseRegex);
        if (!SessionSearch.TryCreate(options, out var search, out var error))
        {
            FindStatus = error ?? string.Empty;
            return;
        }

        _isFinding = true;
        FindStatus = "Searching…";
        try
        {
            var rows = Sessions.ToList();
            var matches = await Task.Run(() => rows.Where(row => search!.Matches(row.Session)).ToHashSet());

            foreach (var row in Sessions)
            {
                row.IsFindMatch = matches.Contains(row);
            }

            var visible = FilteredSessions.ToHashSet();
            var hidden = matches.Count(row => !visible.Contains(row));
            FindStatus = matches.Count switch
            {
                0 => "No sessions match.",
                1 => "1 session matches.",
                _ => $"{matches.Count} sessions match.",
            } + (hidden > 0 ? $" {hidden} hidden by the filter." : string.Empty);

            FindResultsChanged?.Invoke();
            if (FilteredSessions.FirstOrDefault(row => row.IsFindMatch) is { } first)
            {
                SelectedSessionRow = first;
            }
        }
        finally
        {
            _isFinding = false;
        }
    }

    /// <summary>Selects the next highlighted session after the selected one, wrapping round.</summary>
    private void FindNext()
    {
        if (FilteredSessions.Count == 0)
        {
            return;
        }

        var start = SelectedSessionRow is { } selected ? FilteredSessions.IndexOf(selected) : -1;
        for (var step = 1; step <= FilteredSessions.Count; step++)
        {
            var row = FilteredSessions[(start + step + FilteredSessions.Count) % FilteredSessions.Count];
            if (row.IsFindMatch)
            {
                SelectedSessionRow = row;
                return;
            }
        }

        if (!string.IsNullOrEmpty(FindText))
        {
            _ = FindAsync();
        }
    }

    /// <summary>Hides the Find bar and removes the highlighting.</summary>
    private void CloseFind()
    {
        IsFindBarVisible = false;
        FindStatus = string.Empty;
        var changed = false;
        foreach (var row in Sessions)
        {
            changed |= row.IsFindMatch;
            row.IsFindMatch = false;
        }

        if (changed)
        {
            Dispatcher.UIThread.Post(() => FindResultsChanged?.Invoke());
        }
    }
}
