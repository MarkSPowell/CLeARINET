// CLeARINET dev host -- Phase 1 HTTPS decryption spike.
//
// This is throwaway tooling to validate the interception certificate
// design against real traffic, not a preview of the product's UI or CLI.
// See docs/CLeARINET Interception Certificate Design for the design this
// is proving out, and the project plan's Phase 1 exit criterion for what
// "done" looks like.

using Clearinet.ProxyCore.Certificates;
using Clearinet.ProxyCore.Proxy;

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
const int port = 8888;
var proxy = new InterceptingProxyListener(port, leafProvider);
proxy.Start();

Console.WriteLine($"Listening on 127.0.0.1:{port}.");
Console.WriteLine($"Point a browser's HTTPS proxy (or the Windows system proxy) at 127.0.0.1:{port},");
Console.WriteLine("then browse to any HTTPS site.");
Console.WriteLine("Decrypted request lines will print below as they come through.");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

await Task.Delay(Timeout.Infinite);
return 0;
