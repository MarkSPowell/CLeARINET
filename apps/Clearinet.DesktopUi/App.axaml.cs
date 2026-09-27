using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Clearinet.CompatShim;
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
                new MacTrustConfirmationWindow().ShowDialog<bool>(mainWindow!).GetAwaiter().GetResult(),
            // Import/Export via Extension's format picker. Awaited, not
            // blocked on: the view model's command handlers are async, so
            // this runs as an ordinary modal dialog on the UI thread.
            chooseFormat: (heading, choices, rememberedKey) =>
                new FormatPickerWindow(heading, choices, rememberedKey)
                    .ShowDialog<Clearinet.Compatibility.Extensions.FormatChoice?>(mainWindow!));

        mainWindow = new MainWindow { DataContext = viewModel };

        // The two compatibility-layer hooks that need a real window: a
        // ported extension asking the user for a file
        // (Utilities.ObtainOpenFilename) or telling them something
        // (FiddlerApplication.DoNotifyUser). Both use Avalonia, so they
        // behave the same on Windows and macOS. The VM already set the
        // preferences and log hooks.
        var owner = mainWindow;
        CompatShimHost.PromptForOpenFile = (title, filter) => PromptForOpenFile(owner, title, filter);
        CompatShimHost.NotifyUser = (message, title) =>
            Dispatcher.UIThread.Post(() => viewModel.ShowExtensionNotice(title, message));

        // The proxy listener and its TcpListener socket need an explicit
        // Stop() -- there's no window "closed" event that would run this
        // for us for free, so it's wired up here rather than left to
        // finalization.
        desktop.ShutdownRequested += (_, _) => viewModel.Dispose();

        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }

    /// <summary>
    /// Shows Avalonia's own file picker (native on both Windows and macOS)
    /// for an extension that asked for a file, and blocks the calling thread
    /// until the user picks or cancels. That's safe only because extension
    /// imports run on a background thread (see
    /// MainWindowViewModel.ImportViaExtension); if it's ever called on the UI
    /// thread, blocking would deadlock, so it declines instead.
    /// </summary>
    private static string? PromptForOpenFile(Window owner, string title, string filter)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Console.WriteLine("[Extension] An extension asked for a file from the UI thread; that would deadlock, so no file was chosen.");
            return null;
        }

        var chosen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var fileTypes = Clearinet.CompatShim.Utilities.ParseFileDialogFilter(filter)
                    .Select(f => new FilePickerFileType(f.Description) { Patterns = f.Patterns })
                    .Append(FilePickerFileTypes.All)
                    .ToList();

                var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter = fileTypes,
                });

                chosen.SetResult(files.Count > 0 ? files[0].Path.LocalPath : null);
            }
            catch (Exception ex)
            {
                // Never let a picker failure escape an async-void callback on
                // the UI thread; the extension just sees "no file".
                Console.WriteLine($"[Extension] File picker failed: {ex.Message}");
                chosen.TrySetResult(null);
            }
        });

        return chosen.Task.GetAwaiter().GetResult();
    }
}
