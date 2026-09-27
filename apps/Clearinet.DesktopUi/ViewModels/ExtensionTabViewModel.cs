namespace Clearinet.DesktopUi.ViewModels;

/// <summary>
/// A tab an extension added with
/// <see cref="Clearinet.Compatibility.Extensions.ExtensionUi.AddTab"/>:
/// its title, and the view the extension supplied (expected to be an
/// Avalonia <c>Control</c>; see MainWindow.axaml.cs for what happens if it
/// isn't).
/// </summary>
public sealed record ExtensionTabViewModel(string Title, object View);
