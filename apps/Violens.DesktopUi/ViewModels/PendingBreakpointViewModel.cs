using Violens.ProxyCore.Breakpoints;

namespace Violens.DesktopUi.ViewModels;

/// <summary>One paused session in the breakpoints panel: editable text plus Resume and Abort.</summary>
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
    /// The whole message as editable text. Text that doesn't parse is kept as
    /// typed, with <see cref="ValidationError"/> set and Resume disabled.
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
