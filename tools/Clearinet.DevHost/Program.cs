// CLeARINET dev host -- Phase 1 HTTPS decryption spike.
//
// This is throwaway tooling to validate the interception certificate
// design against real traffic, not a preview of the product's UI or CLI.
// See docs/CLeARINET Interception Certificate Design for the design this
// is proving out, and the project plan's Phase 1 exit criterion for what
// "done" looks like.

using System.Net.Sockets;
using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Proxy;
using Clearinet.ProxyCore.Sessions;

Console.WriteLine("CLeARINET dev host -- Phase 1 HTTPS decryption spike");
Console.WriteLine();

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("This spike currently only implements the Windows trust-store path");
    Console.WriteLine("from the Interception Certificate Design doc. The macOS Keychain path");
    Console.WriteLine("is still a to-do -- see that doc's open-risks checklist.");
    return 1;
}

var authority = new CertificateAuthority();
Console.WriteLine($"Root CA ready: {authority.RootCertificate.Subject}");
Console.WriteLine($"  Thumbprint:  {authority.RootCertificate.Thumbprint}");
Console.WriteLine($"  Valid until: {authority.RootCertificate.NotAfter:u}");
Console.WriteLine();
Console.WriteLine("If this is the first run, Windows should have just asked whether to");
Console.WriteLine("trust this certificate -- that's the native install prompt the design");
Console.WriteLine("doc calls for, not a bug.");
Console.WriteLine();

var leafProvider = new LeafCertificateProvider(authority.RootCertificate);
var sessionStore = new SessionStore();

// Defaults to 8888; pass a different preferred port as the first argument,
// e.g.:
//   dotnet run --project tools\Clearinet.DevHost -- 8899
// If the preferred port is already taken -- most often another CLeARINET
// process still running, DesktopUi included -- StartOnAvailablePort falls
// back to whatever free port the OS hands out rather than failing outright.
var preferredPort = args.Length > 0 && int.TryParse(args[0], out var parsedPort) ? parsedPort : 8888;

InterceptingProxyListener proxy;
try
{
    proxy = InterceptingProxyListener.StartOnAvailablePort(preferredPort, leafProvider, sessionStore);
}
catch (SocketException ex)
{
    Console.WriteLine($"Couldn't start the proxy: {ex.Message}");
    return 1;
}

var port = proxy.Port;
if (port != preferredPort)
{
    Console.WriteLine($"Port {preferredPort} was already in use -- probably another CLeARINET process still running.");
}

Console.WriteLine($"Listening on 127.0.0.1:{port}.");
Console.WriteLine($"Point a browser's HTTPS proxy at 127.0.0.1:{port} (not the Windows system proxy --");
Console.WriteLine("that also routes this machine's other apps through here, which breaks anything that");
Console.WriteLine("doesn't trust the CLeARINET root, Claude Desktop included), then browse to any HTTPS site.");
Console.WriteLine("Captured sessions will print below as they complete: # status method URL (sizes).");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

Console.CancelKeyPress += (_, args) =>
{
    // Suppress the default immediate-terminate behavior so the SAZ write
    // below actually gets to run. This is synchronous, plain file I/O --
    // no async work in flight to race against -- so there's nothing unsafe
    // about doing it directly in the handler; Environment.Exit(0) at the
    // end then terminates explicitly once it's done, rather than leaving
    // the runtime to decide when (or whether) that happens on its own.
    args.Cancel = true;

    var sessions = sessionStore.Snapshot();
    Console.WriteLine();
    Console.WriteLine($"Captured {sessions.Count} session(s) this run.");

    if (sessions.Count > 0)
    {
        var path = Path.Combine(Environment.CurrentDirectory, $"clearinet-capture-{DateTime.Now:yyyyMMdd-HHmmss}.saz");
        SazWriter.Write(path, sessions);
        Console.WriteLine($"Saved: {path}");
        Console.WriteLine("Try opening that in Fiddler Classic, if you still have it installed --");
        Console.WriteLine("that's the actual Phase 1 exit criterion.");
    }

    Environment.Exit(0);
};

await Task.Delay(Timeout.Infinite);
return 0;
