using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Violens.DesktopUi;

/// <summary>
/// Asks before trusting the root certificate on macOS, because
/// <c>security add-trusted-cert</c> has no prompt of its own (Windows shows
/// one).
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
