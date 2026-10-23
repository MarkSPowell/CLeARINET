namespace Violens.DesktopUi.ViewModels;

/// <summary>A tab an extension added: its title and its view (expected to be an Avalonia control).</summary>
public sealed record ExtensionTabViewModel(string Title, object View);
