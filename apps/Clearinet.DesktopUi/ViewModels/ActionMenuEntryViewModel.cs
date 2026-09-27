namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// One non-checkable, click-to-run entry -- backs both the dynamic
/// "_Tools" menu (built from a loaded script's
/// <c>FiddlerScriptRunner.Directives.ToolsActions</c>) and the session
/// grid's right-click context menu (built from
/// <c>Directives.ContextActions</c>). Both are just "a label, something
/// that happens when it's clicked," so one class covers both rather than
/// two near-identical ones -- unlike <see cref="RulesMenuEntryViewModel"/>,
/// there's no checked state or radio-group behavior to carry.
/// </summary>
public sealed class ActionMenuEntryViewModel(string header, Action invoke)
{
    public string Header { get; } = header;

    public RelayCommand InvokeCommand { get; } = new(invoke);
}
