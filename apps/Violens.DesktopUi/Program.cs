using Avalonia;
using Violens.AppCore.Migration;
using Violens.ProxyCore;

namespace Violens.DesktopUi;

// VioLens desktop app -- Phase 2 MVP entry point.
//
// This is the real product shell the project plan's architecture diagram
// calls "Desktop UI, Windows first": it hosts the same proxy core
// (CertificateAuthority, LeafCertificateProvider, SessionStore,
// InterceptingProxyListener) that Violens.DevHost's console spike proved
// out for Phase 1, but shows captured sessions in a live list instead of
// console lines. See MainWindowViewModel for where the proxy actually
// starts.
internal static class Program
{
    // Avalonia's own startup convention: BuildAvaloniaApp() is looked up by
    // name by the Avalonia designer/previewer tooling, so its signature and
    // name are kept exactly as the framework expects even though nothing
    // else in this codebase calls it directly.
    [STAThread]
    public static void Main(string[] args)
    {
        // VioLens's own names for what the engine creates (its root
        // certificate, data folder, proxy page), and its log on the console.
        ProxyEngine.Configure(ProxyHostIdentity.Default, log: entry => Console.WriteLine($"{entry.Time:HH:mm:ss} {entry}"));

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
