# Embedding ProxyCore

`Violens.ProxyCore` is the engine inside VioLens: the part that listens as a
proxy, decrypts HTTPS, records each request and response, and lets code look
at and change traffic on its way through. It has no user interface, so you
can build your own debugger, capture tool or test harness on it, in Windows
Forms, WPF, Avalonia, a console app or a service.

This guide is for developers doing that, particularly anyone building a
Fiddler-style tool. It walks through a small working app; the complete code
is in [`samples/EmbeddingSample`](../samples/EmbeddingSample), which builds
with the rest of the solution so it always matches the real API.

- [What you get](#what-you-get)
- [Requirements](#requirements)
- [Installing](#installing)
- [A minimal app](#a-minimal-app)
- [Your app's identity](#your-apps-identity)
- [Logging](#logging)
- [The root certificate](#the-root-certificate)
- [Seeing traffic](#seeing-traffic)
- [Changing traffic: per-request hooks](#changing-traffic-per-request-hooks)
- [Breakpoints](#breakpoints)
- [AutoResponder](#autoresponder)
- [The system proxy and upstream proxies](#the-system-proxy-and-upstream-proxies)
- [Phones and other computers](#phones-and-other-computers)
- [Sending requests: Composer and Replay](#sending-requests-composer-and-replay)
- [Saving and opening captures](#saving-and-opening-captures)
- [Apps on .NET Framework](#apps-on-net-framework)
- [Versions and compatibility](#versions-and-compatibility)

## What you get

| Area | Types |
|---|---|
| Listening, HTTPS interception | `InterceptingProxyListener`, `CertificateAuthority`, `LeafCertificateProvider` |
| Captured traffic | `SessionStore`, `Session`, `CapturedRequest`, `CapturedResponse`, `SessionTimings`, `WebSocketLog` |
| Changing traffic | `IExtensionSessionHost` (per-request hooks), `IExtensionAutoTamperHost`, `IScriptRunner` |
| Pausing and editing | `BreakpointManager`, `PendingBreakpoint` |
| Answering without the server | `AutoResponderRules` |
| Networks | `SystemProxyController`, `UpstreamProxy`, `ProxyAutoConfig` (PAC and WPAD), proxy sign-in |
| Files | `SazReader`/`SazWriter`, `HarReader`/`HarWriter` |
| Helpers | `ContentDecoder` (gzip, deflate, Brotli, zstd), `HttpMessageText` (raw text), `SessionUrl`, `ComposerClient` |
| Settings | `PreferenceStore`, `IPreferenceStore` |

It speaks HTTP/1.1 to clients and servers, including WebSockets (messages are
recorded, permessage-deflate included). HTTP/2 isn't supported yet.

## Requirements

- **.NET 10.** ProxyCore uses TLS and compression features that .NET
  Framework lacks. Windows Forms and WPF both run on .NET 10, so a desktop
  app can move across. An app that stays on .NET Framework uses the engine
  through a separate host process instead: see
  [Apps on .NET Framework](#apps-on-net-framework).
- **Windows or macOS** for the parts that touch the operating system
  (trusting the root certificate, setting the system proxy). Capturing
  traffic sent straight to the proxy works on Linux too.

## Installing

```
dotnet add package Violens.ProxyCore --prerelease
```

Versions match VioLens releases (0.2.0-preview.1 and so on). While the
version starts with 0, a new minor version may change the public API; the
release notes list what changed. See [Versions and compatibility](#versions-and-compatibility).

## A minimal app

```csharp
using Violens.ProxyCore;
using Violens.ProxyCore.Certificates;
using Violens.ProxyCore.Proxy;
using Violens.ProxyCore.Sessions;
using Violens.ProxyCore.SystemProxy;

// Who we are: our own root certificate, data folder and proxy page.
ProxyEngine.Configure(new ProxyHostIdentity("Contoso Web Debugger"),
    log: entry => Console.WriteLine(entry));

// Put the user's proxy settings back if our last run crashed.
SystemProxyController.RecoverFromCrash();

var authority = new CertificateAuthority();           // creates and trusts the root
var sessions = new SessionStore();
sessions.SessionAdded += s =>
    Console.WriteLine($"#{s.Id} {s.Response.StatusCode} {SessionUrl.Of(s)}");

var listener = InterceptingProxyListener.StartOnAvailablePort(
    8888, new LeafCertificateProvider(authority.RootCertificate), sessions);

SystemProxyController.Enable(listener.Port);          // optional
Console.ReadLine();
listener.Stop();
SystemProxyController.Disable();
```

That's a working HTTPS-decrypting proxy. The rest of this guide covers each
piece.

## Your app's identity

`ProxyEngine.Configure` tells ProxyCore whose app it's running in. Call it
once, at startup, before creating a `CertificateAuthority` or touching the
system proxy.

```csharp
ProxyEngine.Configure(new ProxyHostIdentity("Contoso Web Debugger") { ProductVersion = "1.0" });
```

From the product name, `ProxyHostIdentity` derives:

| Setting | For "Contoso Web Debugger" | Used for |
|---|---|---|
| `RootCertificateName` | `DO_NOT_TRUST_ContosoWebDebuggerRoot` | The root certificate's name; found again by it on later runs |
| `ResolveDataFolder()` | `%LOCALAPPDATA%\Contoso Web Debugger` (macOS: `~/Library/Application Support/...`) | The system-proxy crash-recovery backup, and `PreferenceStore.CreateDefault()` |
| `ProxyHostName` | `contosowebdebugger` | The proxy's own page, `http://contosowebdebugger/` |
| `RootCertificateFileName` | `ContosoWebDebuggerRoot.cer` | The certificate download on that page |
| `ProductName`, `ProductVersion` | as given | Error pages, messages, the HAR creator field |

Override any of them with an object initializer, for example to keep a root
certificate name your app already uses:

```csharp
new ProxyHostIdentity("Contoso Web Debugger") { RootCertificateName = "DO_NOT_TRUST_ContosoRoot" }
```

Without `Configure`, ProxyCore uses VioLens's own identity. Don't ship that:
your app and VioLens would share one root certificate and one crash-recovery
file, and each would think it owned them.

## Logging

ProxyCore is silent unless you give it somewhere to log:

```csharp
ProxyEngine.Configure(identity, log: entry => myLog.Write(entry.Category, entry.Message));
```

Each `ProxyLogEntry` has a `Time`, a `Category` and a `Message`. Categories
are `proxy` (connections and failures), `session` (a line per completed
request), `system-proxy` and `pac`. The handler runs on
whatever thread produced the message, often a connection's, so make it
thread-safe and quick; if it throws, the message is dropped.

With `Microsoft.Extensions.Logging`:

```csharp
var logger = loggerFactory.CreateLogger("ProxyCore");
ProxyEngine.Configure(identity, log: e => logger.LogInformation("[{Category}] {Message}", e.Category, e.Message));
```

## The root certificate

`new CertificateAuthority()` finds your app's root in the current user's
personal certificate store, or creates one (an ECDSA P-256 key, valid for five
years, never exportable), then makes sure it's trusted:

- **Windows:** adding it to the current user's trusted roots shows Windows'
  own "Do you want to install this certificate?" prompt. If the user says No,
  the constructor throws.
- **macOS:** trusting it adds it to the login keychain with no prompt from the
  system, so **ask the user yourself first**. Pass `installToTrustStore: false`
  to get the certificate without touching trust, for example to show its
  details in your confirmation dialog.

`authority.Uninstall()` removes it from both stores, for a "Remove
certificate" menu command or your uninstaller. Create one
`CertificateAuthority` per run and reuse it: creating the authority is what
can prompt; minting the per-site certificates (`LeafCertificateProvider`) is
cheap and cached.

## Seeing traffic

Every finished request and response goes into the `SessionStore` you passed
to the listener, as an immutable `Session`:

```csharp
sessions.SessionAdded += session =>
{
    // session.Id, session.Host, session.Scheme ("https" or "http")
    // session.Request / session.Response: method, target, headers, body bytes
    // session.Timings: DNS, connect, TLS, wait and download times
    // session.Flags: string flags set by your hooks (see below)
    // session.WebSocket: the messages, for an upgraded connection
};
```

Bodies are kept as they came over the wire. To read one, decode it:

```csharp
var encoding = ContentDecoder.FindHeader(session.Response.Headers, "Content-Encoding");
if (ContentDecoder.TryDecode(session.Response.Body, encoding, out var bytes, out var error))
{
    var text = ContentDecoder.DecodeText(bytes, ContentDecoder.FindHeader(session.Response.Headers, "Content-Type"));
}
```

`HttpMessageText.Format(session.Request)` gives the raw text of a request or
response, as a Raw inspector shows it.

**Threads.** `SessionAdded` is raised on the connection's thread. In Windows
Forms, hand it to the UI thread, and batch: a busy page can produce hundreds
of sessions a second.

```csharp
private readonly ConcurrentQueue<Session> _incoming = new();

sessions.SessionAdded += session => _incoming.Enqueue(session);

// A System.Windows.Forms.Timer, every 250 ms, on the UI thread:
private void OnTick(object? sender, EventArgs e)
{
    if (_incoming.IsEmpty) return;
    sessionList.BeginUpdate();
    while (_incoming.TryDequeue(out var session)) AddRow(session);
    sessionList.EndUpdate();
}
```

`SessionStore.Snapshot()` returns everything captured so far, `Remove` and
`Clear` delete sessions (raising `SessionsRemoved`).

## Changing traffic: per-request hooks

`IExtensionSessionHost` is the hook to use if your app has its own
per-request object, the way Fiddler has its `Session` (or an `Exchange`).
ProxyCore calls `BeginSession` once for each request, and you return an
`IExtensionSession` whose methods it calls in order as the request goes out
and the response comes back. Your object lives from the first call to the
last, so state you keep on it (flags, timers, anything) carries through.

```csharp
public sealed class ExchangeHost(IExchangeRules rules) : IExtensionSessionHost
{
    public bool IsActive => true;

    public IExtensionSession BeginSession(int sessionOrdinal, string hostname, string scheme) =>
        new Hooks(new Exchange(sessionOrdinal, hostname, scheme), rules);
}
```

Pass it to the listener: `StartOnAvailablePort(port, leafs, sessions, sessionHost: exchangeHost)`.

The calls, for one request, and their Fiddler equivalents:

| Order | `IExtensionSession` method | Fiddler equivalent | Can change |
|---|---|---|---|
| 1 | `PeekAtRequestHeaders(request)` | `OnPeekAtRequestHeaders` | Nothing: look only (make header edits in step 2) |
| 2 | `RequestBefore(request)` | `OnBeforeRequest`, `AutoTamperRequestBefore` | The request; or answer it locally |
| 3 | `RequestAfter(request)` | `AutoTamperRequestAfter` | Nothing (already sent) |
| 4 | `PeekAtResponseHeaders(request, response)` | `OnPeekAtResponseHeaders` | Nothing: look only (make header edits in step 5) |
| 5 | `ResponseBefore(request, response)` | `OnBeforeResponse`, `AutoTamperResponseBefore` | The response |
| 6 | `ResponseAfter(request, response)` | `AutoTamperResponseAfter` | Nothing (already returned) |
| end | `Flags` | `oSession["..."]` | Stored on the captured `Session` |

To answer a request without contacting the server (Fiddler's
`utilCreateResponseAndBypassServer`), return the response from step 2:

```csharp
public ExtensionRequestResult RequestBefore(CapturedRequest request)
{
    if (request.Target.StartsWith("/hello", StringComparison.Ordinal))
    {
        exchange["ui-comments"] = "Answered locally";
        return new ExtensionRequestResult(request, LocalResponse: TextResponse(200, "OK", "Hello"));
    }

    // oSession.oRequest["X-Debugged-By"] = "Contoso";
    request = request with { Headers = [.. request.Headers, ("X-Debugged-By", "Contoso")] };
    return new ExtensionRequestResult(request);
}
```

The request and response types are immutable records: change them with
`with` and return the result. Each step is handed the current version, which
something earlier (a script, a breakpoint edit) may have changed, so bring
your own object up to date from it rather than assuming nothing moved.

Things to know:

- **Hooks run on connection threads**, several at once, never on your UI
  thread. Keep them thread-safe and fast: the request waits for them. Never
  block a hook waiting for the UI thread (a `Control.Invoke`, say): that can
  deadlock. To let the user edit a request, use a [breakpoint](#breakpoints).
- **`IsActive` costs.** While it's true, every body is read in full before
  step 2 and step 5, so streamed responses arrive all at once. Return false
  when your app has no rules or extensions loaded.
- **Don't let a hook throw.** ProxyCore doesn't catch it, so the client's
  connection is dropped. Catch inside each hook, log, and carry on with the
  message unchanged; the sample does this.
- **AutoResponder runs first.** A request an AutoResponder rule answers never
  reaches these hooks.

The full order for a request is: AutoResponder, `PeekAtRequestHeaders`, the
`IScriptRunner`'s `OnBeforeRequest`, `IExtensionAutoTamperHost` handlers,
`RequestBefore`, the request breakpoint, then sending. Responses mirror that.

**Two simpler hooks.** `IExtensionAutoTamperHost` is the same set of points
without the per-request object (each call stands alone). `IScriptRunner` is
for a scripting engine, with just `OnBeforeRequest` and `OnBeforeResponse`;
VioLens's FiddlerScript support (`Violens.Compatibility`) implements it. You
can pass all three to one listener.

The sample's [`Exchanges.cs`](../samples/EmbeddingSample/Exchanges.cs) has a
complete `Exchange` class with Fiddler-style flags, the hooks, and a rules
interface in the shape of CustomRules.js.

## Breakpoints

```csharp
var breakpoints = new BreakpointManager();
breakpoints.Rules.RequestUrlContains = "/api/";    // like QuickExec's "bpu /api/"
breakpoints.Rules.ResponseStatusCodeEquals = 500;  // like "bps 500"

breakpoints.BreakpointHit += pending =>
    uiContext.Post(_ => ShowBreakpointEditor(pending), null);
```

Each rule you set is its own trigger: any one matching pauses the message.
Pass `breakpointManager: breakpoints` to the listener. When a rule matches,
that request (or response) waits, and `BreakpointHit` gives you a
`PendingBreakpoint`:

- `RawText` is the message as text for an editor; `TryEdit(newText)` checks
  and applies an edit (`ValidationError` says what's wrong if it can't).
- `Resume()` lets it go on, edited or not; `Abort()` drops it.

Only that request waits; everything else keeps flowing. `BreakpointHit` is
raised on the connection's thread, hence the `Post` to the UI thread.

## AutoResponder

`AutoResponderRules` answers matching requests without contacting the server,
using Fiddler's rule syntax:

```csharp
var autoResponder = new AutoResponderRules { IsEnabled = true };
autoResponder.Rules.Add(new AutoResponderRule { MatchPattern = "EXACT:https://example.com/app.js", Action = @"C:\fixes\app.js" });
autoResponder.Rules.Add(new AutoResponderRule { MatchPattern = "regex:(?i)/ads/", Action = "*drop" });
autoResponder.Rules.Add(new AutoResponderRule { MatchPattern = "/slow", Action = "*delay:2000" });
```

Patterns: a plain substring, `EXACT:`, `regex:`, `NOT:` and `METHOD:`.
Actions: a file, a URL to fetch instead, `*redir:`, `*delay:`, `*header:`,
`*flag:`, `*drop`, `*reset`, `*bpu`, `*bpafter`, `*CORSPreflightAllow` and
`*exit`. Pass `autoResponderRules: autoResponder` to the listener; rules can
change while it runs.

## The system proxy and upstream proxies

```csharp
SystemProxyController.RecoverFromCrash();                  // at startup, always
var upstream = SystemProxyController.DetectUpstream(listener.Port);
listener.Gateway = upstream.Proxy;                          // forward through the user's own proxy
SystemProxyController.Enable(listener.Port);
// ...
SystemProxyController.Disable();
```

`Enable` saves the current settings to a backup file in your data folder
before changing anything, and `Disable` puts them back and deletes the
backup. If your app crashes in between, the backup survives, and
`RecoverFromCrash` at the next start restores it. On Windows this sets the
user's WinINET proxy (what browsers and most apps use); on macOS, each
network service's web proxies.

Corporate networks:

- `listener.Gateway = UpstreamProxy.Parse("proxy.contoso.com:8080")` sends
  everything through a proxy, with its bypass list honoured.
- `listener.AutoConfig = ProxyAutoConfig.FromScript(pacText)` (or
  `ProxyAutoConfig.Create(autoDetect: true, scriptUrl, fallback)` for WPAD)
  asks a PAC script which proxy to use for each connection.
- Proxies that ask for a sign-in get Windows' own (Negotiate or NTLM) by
  default; set `listener.ProxyCredential` for a user name and password.

## Phones and other computers

Pass `allowRemoteClients: true` to the listener to accept connections from
other devices, not just this computer. On such a device, set the proxy to
this computer's address and port, then browse to `http://contosowebdebugger/`
(your `ProxyHostName`): the page there offers your root certificate, with
steps for iPhone and Android.

## Sending requests: Composer and Replay

`ComposerClient` sends a request through your own listener, so the result is
captured like any other session:

```csharp
var edited = session.Request with { Target = SessionUrl.Of(session) };   // absolute URL
if (ComposerClient.TryResolve(edited, out var url, out var toSend, out var error))
{
    var response = await ComposerClient.SendAsync(
        listener.Port, leafProvider.IssuerCertificateDer, url!, toSend!, cancellationToken);
}
```

Bodies are sent byte for byte, so binary bodies replay exactly.

## Saving and opening captures

```csharp
SazWriter.Write(@"C:\captures\today.saz", sessions.Snapshot());
var result = await SazReader.ImportAsync(@"C:\captures\old.saz", sessions);  // adds to the store
HarWriter.Write(@"C:\captures\today.har", sessions.Snapshot());
await HarReader.ImportAsync(@"C:\captures\from-browser.har", sessions);
```

SAZ files open in Fiddler Classic and VioLens, and Fiddler's SAZ files open
here. HAR files include timings and WebSocket messages. Session flags aren't
written to SAZ files yet.

## Apps on .NET Framework

ProxyCore needs .NET 10, so a .NET Framework app can't load it directly.
Instead, it can run the engine in a process of its own: **Violens.ProxyHost**,
a small .NET 10 program, hosts ProxyCore, and your app talks to it through
**Violens.ProxyHost.Client**, a library for .NET Standard 2.0 (so .NET
Framework 4.6.2 and later) with no other dependencies.

| Part | What it is | Where to get it |
|---|---|---|
| `Violens.ProxyHost.Client` | The library your app references | NuGet |
| `Violens.ProxyHost.exe` | The host, self-contained (no .NET 10 install needed) | `Violens.ProxyHost-<version>-win-x64.zip` on each release; ship it in a folder beside your app |

```csharp
using Violens.ProxyHost.Client;

// Starts the host and connects to it over a private named pipe.
var proxy = await ProxyHostClient.LaunchAsync(
    Path.Combine(Application.StartupPath, @"ProxyHost\Violens.ProxyHost.exe"));

proxy.SessionCaptured += (s, e) => BeginInvoke(new Action(() => AddRow(e.Session)));
proxy.Log += (s, e) => Debug.WriteLine(e);

// Like OnBeforeRequest in CustomRules.js.
proxy.OnBeforeRequest = ctx =>
{
    ctx.Request.SetHeader("X-Debugged-By", "Contoso");
    if (ctx.Host == "example.com" && ctx.Request.Target.StartsWith("/hello"))
    {
        ctx.Flags["ui-comments"] = "Answered locally";
        ctx.RespondWith(ProxyResponse.Text(200, "OK", "Hello from the proxy"));
    }
};

var started = await proxy.StartAsync(
    new AppIdentity("Contoso Web Debugger"),
    new StartOptions { UseSystemProxy = true, HookRequests = true });

// ... later
await proxy.StopAsync();
proxy.Dispose();   // the host puts the system proxy back and exits
```

What it covers:

- **Capturing.** `StartAsync` takes the same identity settings as
  `ProxyHostIdentity` (`AppIdentity`) and creates and trusts the root
  certificate (the Windows prompt appears from the host process). It can
  become the system proxy, going through the user's own proxy.
- **Sessions.** `SessionCaptured` brings each finished request and response,
  bodies included (turn them off with `StartOptions.IncludeBodies` to save
  memory), with its full URL and flags.
- **Hooks.** `OnBeforeRequest` and `OnBeforeResponse` see each request and
  response, change them in place (`SetHeader`, `RemoveHeader`,
  `SetBodyText`), set flags, or answer the request locally (`RespondWith`).
  Turn them on with `StartOptions.HookRequests` and `HookResponses`.
- **Breakpoints.** `SetBreakpointsAsync` sets the same rules as QuickExec's
  `bpu`, `bpafter`, `bps` and `bpm`. `BreakpointHit` gives the paused
  message's raw text; call `Resume(editedText)` or `Abort()` when the user is
  done.
- **Files.** `SaveSazAsync`, `LoadSazAsync` and `ClearSessionsAsync`.
- **The certificate.** `StartResult.RootCertificate` for showing or
  exporting it; `UninstallRootCertificateAsync` to remove it.

Things to know:

- **Hooks cross a process boundary.** Each one is a round trip over the pipe,
  quick on one machine but not free: turn hooks on only while there are
  rules to run. A hook that doesn't answer within `StartOptions.HookTimeout`
  (10 seconds by default) is skipped, the message goes on unchanged, and the
  host logs why.
- **Threads.** Events arrive on the client's own reader thread, in order:
  hand them to the UI thread with `BeginInvoke`, never `Invoke`. Hooks run on
  thread-pool threads, several at once; don't make them wait for the UI.
- **Lifetime.** The host stops capturing, puts the system proxy back and
  exits when you dispose the client, when the pipe breaks, or when your
  process ends, even if it crashes. If the host itself crashes while it's
  the system proxy, the next `StartAsync` restores the user's settings.
- **Only your app can talk to it.** `LaunchAsync` gives the host a random
  pipe name and a one-time token, and refuses a pipe that answers without
  that token.

Moving the app itself to .NET 10 is the other route. Windows Forms and WPF
are fully supported there; what usually holds a Fiddler-style app back is
the rest: JScript.NET (for FiddlerScript) doesn't exist on .NET 10, runtime
compilation through CodeDOM isn't supported, and extensions built for .NET
Framework may not load. VioLens handles those by translating FiddlerScript to
run on a JavaScript engine (`Violens.Compatibility`) and running old
extensions in a separate .NET Framework process.

## Versions and compatibility

- The package version is the VioLens version it shipped with.
- **0.x:** a minor version (0.2 to 0.3) may change the public API; a patch
  version (0.2.0 to 0.2.1) only fixes things. Each release's notes have a
  "ProxyCore changes" section.
- **From 1.0:** only a major version (1.x to 2.0) breaks anything.
- Pin a version and move up when you're ready: NuGet never upgrades on its
  own, and a published version never changes.
- Expect HTTP/2 to change how requests are represented before 1.0.

ProxyCore is MIT licensed, like the rest of VioLens. Questions and problems:
[open an issue](https://github.com/MarkSPowell/VioLens/issues).
