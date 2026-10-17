using Clearinet.DesktopUi.Models;
using Clearinet.ProxyCore.QuickExec;

namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// QuickExec, the command box under the session list (Alt+Q), as in Fiddler
/// Classic. Built-in commands run here (see <see cref="QuickExecParser"/>);
/// anything else is offered to the loaded FiddlerScript's
/// <c>OnExecAction</c>, then to extensions' <c>IHandleExecAction</c>.
/// Up and Down go through earlier commands.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const int QuickExecHistoryLimit = 50;

    private readonly List<string> _quickExecHistory = [];
    private int _quickExecHistoryPosition;
    private string _quickExecText = string.Empty;
    private RelayCommand? _runQuickExecCommand;

    /// <summary>Raised with the sessions a selection command picked, for the window to select in the list.</summary>
    public event Action<IReadOnlyList<SessionRow>>? QuickExecSelectionRequested;

    public string QuickExecText
    {
        get => _quickExecText;
        set => SetField(ref _quickExecText, value ?? string.Empty);
    }

    /// <summary>Runs <see cref="QuickExecText"/> (Enter in the box).</summary>
    public RelayCommand RunQuickExecCommand => _runQuickExecCommand ??= new RelayCommand(() =>
    {
        var line = QuickExecText.Trim();
        if (line.Length == 0)
        {
            return;
        }

        _quickExecHistory.Remove(line);
        _quickExecHistory.Add(line);
        if (_quickExecHistory.Count > QuickExecHistoryLimit)
        {
            _quickExecHistory.RemoveAt(0);
        }

        _quickExecHistoryPosition = _quickExecHistory.Count;
        QuickExecText = string.Empty;
        RunQuickExec(line);
    });

    /// <summary>Up (-1) or Down (+1) in the box: the previous or next command typed.</summary>
    public void BrowseQuickExecHistory(int step)
    {
        if (_quickExecHistory.Count == 0)
        {
            return;
        }

        _quickExecHistoryPosition = Math.Clamp(_quickExecHistoryPosition + step, 0, _quickExecHistory.Count);
        QuickExecText = _quickExecHistoryPosition < _quickExecHistory.Count ? _quickExecHistory[_quickExecHistoryPosition] : string.Empty;
    }

    /// <summary>Runs one QuickExec command, reporting what it did on the status line.</summary>
    public void RunQuickExec(string line)
    {
        if (QuickExecParser.Parse(line) is not { } command)
        {
            return;
        }

        if (command.IsSelection)
        {
            var picked = FilteredSessions.Where(row => QuickExecParser.Matches(command, row.Session)).ToList();
            QuickExecSelectionRequested?.Invoke(picked);
            StatusText = picked.Count == 0
                ? $"QuickExec: no sessions in the list match {line}."
                : $"QuickExec: selected {picked.Count} {(picked.Count == 1 ? "session" : "sessions")} matching {line}.";
            return;
        }

        switch (command.Kind)
        {
            case QuickExecKind.KeepOnlyContentType:
            {
                if (command.Argument.Length == 0)
                {
                    StatusText = "QuickExec: allbut needs a Content-Type to keep, such as allbut image or allbut json.";
                    return;
                }

                var remove = Sessions.Where(row => !QuickExecParser.Matches(command, row.Session)).Select(row => row.Id).ToList();
                var removed = _sessionStore.Remove(remove);
                StatusText = $"QuickExec: removed {removed} {(removed == 1 ? "session" : "sessions")} whose Content-Type doesn't contain \"{command.Argument}\".";
                return;
            }

            case QuickExecKind.Clear:
                _sessionStore.Clear();
                StatusText = "QuickExec: removed all sessions.";
                return;

            case QuickExecKind.Tail:
            {
                var remove = Sessions.OrderBy(row => row.Id).Select(row => row.Id).SkipLast((int)Math.Min(command.Number, int.MaxValue)).ToList();
                var removed = _sessionStore.Remove(remove);
                StatusText = $"QuickExec: kept the newest {command.Number}, removed {removed}.";
                return;
            }

            case QuickExecKind.BreakOnRequestUrl:
                RequestUrlContains = NullIfEmpty(command.Argument);
                ReportBreakpoint(command.Argument, $"requests whose URL contains \"{command.Argument}\"", "bpu");
                return;

            case QuickExecKind.BreakOnResponseUrl:
                ResponseUrlContains = NullIfEmpty(command.Argument);
                ReportBreakpoint(command.Argument, $"responses to URLs containing \"{command.Argument}\"", "bpafter");
                return;

            case QuickExecKind.BreakOnStatus:
                ResponseStatusCodeEquals = command.Argument.Length == 0 ? null : command.Number;
                ReportBreakpoint(command.Argument, $"responses with status {command.Argument}", "bps");
                return;

            case QuickExecKind.BreakOnMethod:
                RequestMethodEquals = NullIfEmpty(command.Argument);
                ReportBreakpoint(command.Argument, $"{command.Argument} requests", "bpm");
                return;

            case QuickExecKind.ResumeAll:
            {
                var paused = PendingBreakpoints.ToList();
                foreach (var pending in paused)
                {
                    if (pending.ResumeCommand.CanExecute(null))
                    {
                        pending.ResumeCommand.Execute(null);
                    }
                }

                StatusText = paused.Count == 0 ? "QuickExec: nothing is paused." : $"QuickExec: let {paused.Count} paused {(paused.Count == 1 ? "session" : "sessions")} continue.";
                return;
            }

            case QuickExecKind.Start:
                if (StartCommand.CanExecute(null))
                {
                    StartCommand.Execute(null);
                }
                else
                {
                    StatusText = IsRunning ? "QuickExec: already capturing." : "QuickExec: can't start; check the port setting.";
                }

                return;

            case QuickExecKind.Stop:
                if (StopCommand.CanExecute(null))
                {
                    StopCommand.Execute(null);
                }
                else
                {
                    StatusText = "QuickExec: not capturing.";
                }

                return;

            case QuickExecKind.Help:
                OpenDocumentationCommand.Execute(null);
                StatusText = "QuickExec commands: ?text  @host  =404  =POST  >40k  <1k  select type  allbut type  cls  tail N  " +
                             "bpu text  bpafter text  bps 404  bpm POST  go  start  stop. The User Guide's QuickExec section has more.";
                return;
        }

        // Not built in: the script's OnExecAction, then extensions.
        if (_fiddlerScriptRunner.InvokeOnExecAction(command.Words))
        {
            StatusText = $"QuickExec: FiddlerScript ran {command.Words[0]}.";
            return;
        }

        foreach (var handler in _extensionHost.ExecActionHandlers)
        {
            try
            {
                if (handler.OnExecAction(line))
                {
                    StatusText = $"QuickExec: {handler.GetType().Name} ran {command.Words[0]}.";
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[QuickExec] {handler.GetType().FullName} failed on '{line}': {ex.Message}");
            }
        }

        StatusText = $"QuickExec: \"{command.Words[0]}\" isn't a command CLeARINET, the loaded FiddlerScript or an extension knows. Type help for the list.";
    }

    private void ReportBreakpoint(string argument, string what, string command)
    {
        if (argument.Length == 0)
        {
            StatusText = $"QuickExec: {command} breakpoint cleared.";
            return;
        }

        ShowAlsoBreakOnRow = true;
        StatusText = $"QuickExec: CLeARINET will pause {what}. Type {command} on its own to stop.";
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
