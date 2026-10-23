using Avalonia;
using Violens.AppCore.Migration;
using Violens.ProxyCore;
using Violens.ProxyCore.Certificates;

namespace Violens.DesktopUi;

// VioLens desktop app entry point. The proxy starts from
// MainWindowViewModel.
internal static class Program
{
    /// <summary>The command-line switch installer/VioLens.iss's [UninstallRun] passes.</summary>
    internal const string RemoveRootCertificateArgument = "--remove-root-certificate";

    // Avalonia's designer finds BuildAvaloniaApp() by name.
    [STAThread]
    public static void Main(string[] args)
    {
        // VioLens's own names for what the engine creates (its root
        // certificate, data folder, proxy page), and its log on the console.
        ProxyEngine.Configure(ProxyHostIdentity.Default, log: entry => Console.WriteLine($"{entry.Time:HH:mm:ss} {entry}"));

        // Run by the Windows uninstaller: remove the root certificate (so
        // nothing VioLens trusted outlives it) and exit, without a window.
        if (args.Contains(RemoveRootCertificateArgument, StringComparer.OrdinalIgnoreCase))
        {
            CertificateAuthority.RemoveRoots(
                ProxyEngine.Identity.RootCertificateName, log: message => Console.WriteLine($"[certificates] {message}"));
            return;
        }

        // Before anything reads a setting or the system-proxy crash
        // recovery runs: brings over what CLeARINET (this app's former
        // name) left behind, the first time only.
        Migration = FormerNameMigration.Run(log: message => Console.WriteLine($"[migration] {message}"));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// What the carry-over from CLeARINET found, for removing CLeARINET's old
    /// root certificate once VioLens's own is trusted (see
    /// MainWindowViewModel). Null when the app wasn't started through Main
    /// (the designer, tests).
    /// </summary>
    internal static FormerNameMigration.Result? Migration { get; private set; }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
