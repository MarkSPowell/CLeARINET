namespace Violens.DesktopUi.ViewModels;

/// <summary>A clickable entry for a script's <c>ToolsAction</c> or <c>ContextAction</c>.</summary>
public sealed class ActionMenuEntryViewModel(string header, Action invoke)
{
    public string Header { get; } = header;

    public RelayCommand InvokeCommand { get; } = new(invoke);
}
