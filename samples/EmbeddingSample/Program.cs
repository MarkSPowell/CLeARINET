// A minimal app built on Violens.ProxyCore: the code walked through in
// docs/Embedding ProxyCore.md. Run it, point a browser's proxy at the port
// it prints (or pass --system-proxy to make it the system proxy while it
// runs), browse, then press Enter to stop and save what was captured.

using Contoso.WebDebugger;
using Violens.ProxyCore;
using Violens.ProxyCore.Breakpoints;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Sessions;
using Violens.ProxyCore.SystemProxy;

var useSystemProxy = args.Contains("--system-proxy");

// 1. Who we are: our own root certificate, data folder and proxy page,
//    never VioLens's. And where the engine's log goes.
ProxyEngine.Configure(
    new ProxyHostIdentity("Contoso Web Debugger") { ProductVersion = "0.1" },
    log: entry => Console.WriteLine($"  {entry}"));

// 2. If a previous run crashed while it was the system proxy, put the
//    user's own proxy settings back before doing anything else.
SystemProxyController.RecoverFromCrash();

// 3. The root certificate for HTTPS interception: created on first run and
//    kept in the user's certificate store. On Windows, trusting it shows the
//    system's own prompt; on macOS, ask the user yourself first (see the guide).
var authority = new CertificateAuthority();
Console.WriteLine($"Root certificate: {authority.RootCertificate.Subject}");

// 4. Where captured sessions go, and the per-request hooks.
var sessions = new SessionStore();
var exchanges = new ExchangeHost(new SampleRules());
var breakpoints = new BreakpointManager();

// Pause requests whose URL contains /api/ before they're sent, as
// QuickExec's "bpu /api/" would. (Each rule set is its own trigger: setting
// RequestMethodEquals too would also pause every request with that method.)
breakpoints.Rules.RequestUrlContains = "/api/";
breakpoints.BreakpointHit += pending =>
{
    // A real app shows pending.RawText in an editor, then calls TryEdit and
    // Resume (or Abort) when the user is done. This sample just lets it go.
    Console.WriteLine($"Paused #{pending.SessionOrdinal} {pending.Request.Method} {pending.Request.Target}; resuming.");
    pending.Resume();
};

sessions.SessionAdded += session =>
{
    // Raised on a connection's thread. A WinForms app would marshal this to
    // the UI thread (Control.BeginInvoke) before touching any controls.
    var colour = session.Flags is { } flags && flags.TryGetValue("ui-backcolor", out var c) ? $" [{c}]" : string.Empty;
    Console.WriteLine($"#{session.Id} {session.Response.StatusCode} {session.Request.Method} {SessionUrl.Of(session)}{colour}");
};

// 5. Start listening. Port 0 lets the OS choose a free port.
var listener = InterceptingProxyListener.StartOnAvailablePort(
    preferredPort: 8888,
    new LeafCertificateProvider(authority.RootCertificate),
    sessions,
    breakpointManager: breakpoints,
    sessionHost: exchanges);
Console.WriteLine($"Listening on 127.0.0.1:{listener.Port}");

// 6. Optionally become the system proxy, going through whatever proxy the
//    user had before (a company proxy, say) rather than straight out.
if (useSystemProxy)
{
    var upstream = SystemProxyController.DetectUpstream(listener.Port);
    listener.Gateway = upstream.Proxy;
    Console.WriteLine($"Upstream: {upstream.Description}");
    SystemProxyController.Enable(listener.Port);
}

Console.WriteLine("Browse, then press Enter to stop.");
Console.ReadLine();

// 7. Stop, put the system proxy back, and save the capture as a SAZ file
//    Fiddler Classic and VioLens can open.
listener.Stop();
if (useSystemProxy)
{
    SystemProxyController.Disable();
}

var captured = sessions.Snapshot();
if (captured.Count > 0)
{
    var path = Path.Combine(Environment.CurrentDirectory, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.saz");
    SazWriter.Write(path, captured);
    Console.WriteLine($"Saved {captured.Count} session(s) to {path}");
}

return 0;
