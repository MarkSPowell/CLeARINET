using Avalonia;
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
        // Before anything reads a setting or the system-proxy crash
        // recovery runs: brings over what CLeARINET (this app's former
        // name) left behind, the first time only.
        FormerNameMigration.Run(log: message => Console.WriteLine($"[Migration] {message}"));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
