using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Clearinet.DesktopUi;

/// <summary>
/// The confirmation dialog the Interception Certificate Design doc's
/// "silent-install tension" section describes: shown once, explicitly,
/// before <c>CertificateAuthority</c>'s constructor is ever reached on
/// macOS, since <c>security add-trusted-cert</c> has no OS-level install
/// prompt the way adding to Windows' <c>CurrentUser\Root</c> store does.
/// See that section for the full reasoning.
///
/// Wired into <see cref="ViewModels.MainWindowViewModel"/>'s constructor
/// as a plain <see cref="Func{TResult}"/> from <c>App.axaml.cs</c>, not a
/// direct reference from the view model to this window -- the view model
/// shouldn't need to know a concrete Avalonia <see cref="Window"/> type
/// exists at all, only that it can ask "did the user agree?" and get a
/// bool back. Constructed and shown fresh every time <c>Start()</c> needs
/// an answer, the same "no state worth preserving between opens" posture
/// <see cref="AboutWindow"/> already takes.
/// </summary>
public partial class MacTrustConfirmationWindow : Window
{
    public MacTrustConfirmationWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void InstallButton_Click(object? sender, RoutedEventArgs e) => Close(true);
}
