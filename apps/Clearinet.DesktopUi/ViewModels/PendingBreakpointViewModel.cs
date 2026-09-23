using Clearinet.ProxyCore.Breakpoints;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// Wraps one <see cref="PendingBreakpoint"/> for the breakpoints panel:
/// exposes its raw text as a bindable, editable property (each edit calls
/// straight through to <see cref="PendingBreakpoint.TryEdit"/>) and its
/// Resume/Abort actions as commands. One of these is created per
/// <c>BreakpointManager.BreakpointHit</c> and discarded on
/// <c>BreakpointResolved</c> -- see MainWindowViewModel.
/// </summary>
public sealed class PendingBreakpointViewModel : ViewModelBase
{
    private readonly PendingBreakpoint _pending;

    public PendingBreakpointViewModel(PendingBreakpoint pending)
    {
        _pending = pending;
        ResumeCommand = new RelayCommand(_pending.Resume, () => _pending.IsValid);
        AbortCommand = new RelayCommand(_pending.Abort);
    }

    public string Title =>
        $"{(_pending.Stage == BreakpointStage.Request ? "Request" : "Response")} #{_pending.SessionOrdinal}  " +
        $"{_pending.Request.Method} {_pending.Host}{_pending.Request.Target}";

    /// <summary>
    /// The whole message as one editable blob (Fiddler Classic's TextView
    /// model -- see HttpMessageText's remarks). Every set re-parses via
    /// <see cref="PendingBreakpoint.TryEdit"/>; an edit that doesn't parse
    /// still updates what's shown (so the person's in-progress typing is
    /// never discarded out from under them) but leaves
    /// <see cref="ValidationError"/> set and <see cref="ResumeCommand"/>
    /// disabled until it's fixed.
    /// </summary>
    public string RawText
    {
        get => _pending.RawText;
        set
        {
            if (_pending.RawText == value)
            {
                return;
            }

            _pending.TryEdit(value);
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(ValidationError));
            ResumeCommand.RaiseCanExecuteChanged();
        }
    }

    public string? ValidationError => _pending.ValidationError;

    public RelayCommand ResumeCommand { get; }

    public RelayCommand AbortCommand { get; }
}
