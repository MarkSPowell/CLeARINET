using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Clearinet.DesktopUi.ViewModels;

namespace Clearinet.DesktopUi;

public partial class App : Application
{
    // How long clearinet.webp's splash stays up before MainWindow takes
    // over. There's no real startup work to wait on (MainWindowViewModel's
    // constructor doesn't touch the network or disk), so this is purely a
    // deliberate beat -- long enough to register as a splash screen, short
    // enough not to feel like a delay.
    private static readonly TimeSpan SplashDuration = TimeSpan.FromMilliseconds(1600);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            splash.Show();

            // Fire-and-forget is deliberate: OnFrameworkInitializationCompleted
            // has to return for Avalonia's own message loop to start pumping,
            // which is what makes the splash window actually paint. Nothing
            // here can be awaited from this method.
            _ = ShowMainWindowAfterSplashAsync(desktop, splash);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowMainWindowAfterSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        await Task.Delay(SplashDuration);

        // Forward-declared and assigned below, rather than the other way
        // around: MainWindowViewModel's constructor needs a
        // confirmMacOSCertificateTrust callback that can own a
        // MacTrustConfirmationWindow by the real MainWindow (so it centers
        // and blocks correctly), but MainWindow itself can't be
        // constructed until after the view model that becomes its
        // DataContext exists. The lambda captures this local by reference,
        // not by value, so it sees the real MainWindow once the assignment
        // a few lines down runs -- and since the callback is only ever
        // invoked later, from MainWindowViewModel.Start() after the user
        // clicks Start (well after mainWindow.Show() below), it's never
        // actually called while still null. See the Interception
        // Certificate Design doc's "silent-install tension" section and
        // MacTrustConfirmationWindow's own remarks for why this dialog
        // exists at all.
        MainWindow? mainWindow = null;

        var viewModel = new MainWindowViewModel(
            confirmMacOSCertificateTrust: () =>
                new MacTrustConfirmationWindow().ShowDialog<bool>(mainWindow!).GetAwaiter().GetResult());

        mainWindow = new MainWindow { DataContext = viewModel };

        // The proxy listener and its TcpListener socket need an explicit
        // Stop() -- there's no window "closed" event that would run this
        // for us for free, so it's wired up here rather than left to
        // finalization.
        desktop.ShutdownRequested += (_, _) => viewModel.Dispose();

        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }
}
