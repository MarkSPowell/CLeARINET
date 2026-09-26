# CLeARINET

An open source HTTP(S) inspection tool for developers — a spiritual
successor to Fiddler Web Debugger (Fiddler Classic), which Progress
Software withdrew in September 2026.

## Status

Early, working MVP. Not yet 1.0 — expect rough edges, and see
[Known limitations](#known-limitations) below for what's deliberately not
here yet. Feedback and issues are welcome.

## What it does today

- Intercepts and decrypts HTTP(S) traffic through a local proxy, using a
  per-install root certificate installed into the current user's trusted
  root store so HTTPS traffic can be inspected — the same mechanism
  Fiddler Classic uses.
- Registers itself as the system proxy while running — WinINET on
  Windows, per network service via `networksetup` on macOS — and recovers
  cleanly if a previous run didn't shut down properly (crash, kill,
  unclean shutdown).
- A session list with live filtering: free-text search across the URL
  and headers (never body content — see the filter grammar's own remarks
  on why), plus `method:`, `host:`, and `status:` query tokens (exact,
  class like `4xx`, comparison, and range forms).
- Request/response inspector tabs: Headers, Raw (decoded text, with
  automatic decompression), Hex, Cookies (each cookie sent or set, with
  its attributes, and a warning for ones browsers will refuse) and Notes
  (flags an importer or extension attached to a session).
- Rows in the session list can be coloured, bold, italic, struck through
  or hidden by an extension or importer (Fiddler's `ui-backcolor` and
  related session flags), and sessions can be removed from the list
  (**Edit > Remove Selected Session / Remove All Sessions**).
- Automatic response decompression covering gzip, deflate (both the
  common zlib-wrapped form and the older raw form some servers still
  send), zstd, and chains of those applied together. bzip2 and the
  historical `compress` encoding are deliberately not supported — neither
  ever saw real use as a web encoding. SDCH is recognized in headers but
  not decoded, since that needs a dictionary exchange this build doesn't
  track. Content compressed with Zopfli needs no special handling — it's
  a slower, better gzip encoder that still produces standard gzip output.
- Fiddler-Classic-style breakpoints: break on all requests and/or all
  responses, or on narrower conditions (URL contains, method, status
  code); inspect and edit a paused message's raw text, then Resume or
  Abort. The breakpoints panel only takes up space once it's actually
  needed.
- AutoResponder: an ordered list of match/action rules (substring,
  wildcard, `EXACT:`/`regex:`/`NOT:`/`METHOD:` matches; redirect, delay,
  header, reset, drop, serve-a-file, and other actions) that can answer a
  matching request without it ever reaching the real server.
- FiddlerScript compatibility: load a `CustomRules.js`-shaped script and
  have it hook `OnBeforeRequest`/`OnBeforeResponse`, declare Rules-menu
  options and string choices, add its own buttons and right-click context
  actions, and add its own columns to the session grid — see the
  FiddlerScript Compatibility Design doc for the full scope and cuts.
- Compiled .NET extension compatibility: drop a compiled extension `.dll`
  (the successor to Fiddler Classic's own extension model) into
  CLeARINET's Extensions folder and it's loaded automatically at
  startup — AutoTamper hooks, request/response inspectors, and session
  import/export are all supported; see the .NET Extension Compatibility
  Design doc.
- A Fiddler-shaped compatibility layer (`Clearinet.CompatShim`) for
  porting real Fiddler Classic extensions from their source, on both
  Windows and macOS. Ported extensions can import sessions, work on live
  traffic (one session object per request, and answering requests
  themselves), and add their own menus, tabs, session-list columns and
  row colours. Working so far:
  - **NetLog importer** (Eric Lawrence's): builds with only its
    `using Fiddler;` lines removed.
  - **CSP Rule Collector**: a CLeARINET-only fork,
    [CSP-CLeARINET-Extension](https://github.com/MarkSPowell/CSP-CLeARINET-Extension),
    with its tab rewritten in Avalonia.
  - **Privacy Scanner** (cookies/P3P): ported as a test of the UI hooks;
    cookie viewing itself is built in (the Cookies tab).

  All three come with the installers as optional extensions, each with
  its own licence: the Windows installer offers them as ticked boxes
  (Privacy Scanner off by default), and the macOS `.dmg` has an
  **Optional Extensions** folder to copy them from. See the User Guide.

  CI builds and tests all three on both platforms. See
  `tests/ExtensionPorts/README.md` and the Extension Test Targets doc.
- Optional legacy extension host: a separate, opt-in tool
  (`tools/Clearinet.LegacyExtensionHost`) that runs real, unmodified-source
  Fiddler Classic extensions against real proxied traffic, bridged to the
  main app over a local IPC connection, for extensions that reach into UI
  surface removed from modern .NET — not part of the main app's own build,
  but available as an unchecked-by-default optional component in the
  Windows installer; see that tool's own README and the .NET Extension
  Compatibility Design doc.
- A **Tools** menu of checkable toggles that show or hide the
  FiddlerScript, Extensions, breakpoint-condition, and AutoResponder
  panels on the main screen, so only what you're actually using takes up
  space.
- Export captured sessions to a `.saz` (Session Archive Zip) file, or
  import a previously saved one back in.
- An automatic or manually-specified listening port (defaults to Auto).
- Settings that persist between runs (port choice, Tools-menu panels,
  filter text and more), stored the same way on Windows and macOS in
  `preferences.json` under `%LOCALAPPDATA%\CLeARINET\` or
  `~/Library/Application Support/CLeARINET/`. Unknown keys are kept, a
  corrupt file is set aside rather than overwritten, and two running
  copies don't clobber each other's changes. See the Preferences Design
  doc.
- **Help > Documentation** in the running app opens the
  [User Guide](docs/User%20Guide.md) — see [Documentation](#documentation)
  below.

## Getting started

Requires the .NET 10 SDK.

```
dotnet restore CLeARINET.sln
dotnet build CLeARINET.sln
dotnet test CLeARINET.sln
dotnet run --project apps/Clearinet.DesktopUi
```

The first time you click Start, you'll be asked to trust a new local root
certificate (named `DO_NOT_TRUST_ClearinetRoot...`, following Fiddler
Classic's own naming convention for the same purpose) — this is what lets
CLeARINET see inside HTTPS traffic on this machine. On Windows that's the
OS's own native prompt; on macOS, where the `security` CLI CLeARINET uses
has no equivalent OS-level prompt, it's a confirmation dialog CLeARINET
shows itself before ever touching your login keychain. Nothing captured
ever leaves the device on its own.

## Documentation

- **Using the app?** Start with the [User Guide](docs/User%20Guide.md) —
  starting/stopping the proxy, the session grid and filtering, breakpoints,
  AutoResponder, FiddlerScript, extensions, and SAZ import/export. It's
  also one click away from inside the running app, via **Help >
  Documentation**.
- **Contributing or curious how something works?** The rest of `docs/` is
  written for that: design rationale, scope cuts, and open follow-ups for
  each area of the app (see [Project layout](#project-layout) below for
  what's there).

## Project layout

- `docs/` — the [User Guide](docs/User%20Guide.md) for using the app, plus
  design docs referenced throughout the code's own comments ("see the
  project plan," "the Interception Certificate Design doc"): the project
  plan and tenets, the interception certificate design, the preferences
  design, the FiddlerScript
  compatibility design, the .NET extension compatibility design, the
  Fiddler feature/tier inventory, and breakpoints research notes.
- `src/Clearinet.ProxyCore` — the proxy engine: the listener, HTTP
  message parsing, sessions, breakpoints, AutoResponder, certificates,
  SAZ export, system-proxy registration on both Windows and macOS, and
  the preferences store.
- `src/Clearinet.Extensibility` — the inspector contract and the built-in
  inspectors (Headers, Raw, Hex, Cookies, Notes).
- `src/Clearinet.Compatibility` — FiddlerScript compatibility
  (`FiddlerScriptRunner`, the `Exchange`/`AppObject` shim, the directive
  scanner behind the Rules menu/Script Actions/custom columns) and
  compiled .NET extension compatibility (`ExtensionHost` and the
  `IFiddlerExtension`-family interfaces), and the Fiddler-shaped
  `Clearinet.CompatShim` layer for extensions ported from source — see both
  design docs above for what's built and what's still scoped out.
- `apps/Clearinet.DesktopUi` — the Avalonia desktop UI.
- `tools/Clearinet.DevHost` — a console host used during early
  development of the proxy core.
- `tools/Clearinet.SampleExtension` — a small, separately-compiled .NET
  extension used to validate `ExtensionHost` against a real `.dll` rather
  than only in-process fakes; see its own README for what it demonstrates
  and how to build it.
- `tools/Clearinet.LegacyExtensionHost` — the optional, separate legacy
  extension host: a `net48` process that can run real, unmodified-source
  Fiddler Classic extensions, bridged to the main app's own proxied
  traffic. Deliberately kept out of `CLeARINET.sln`/the main app's build
  entirely, so nothing about it can affect either — see its own README and
  the .NET Extension Compatibility Design doc.
- `tests/` — unit tests for the projects above, plus `tests/ExtensionPorts`:
  real Fiddler Classic extensions fetched at pinned commits, ported, and
  tested (not part of `CLeARINET.sln`; see its README).

## Known limitations

Listed here on purpose, rather than left for someone to discover the hard
way:

- **macOS support is built, but has had little testing on a real Mac.**
  The first real install found the `.dmg`'s app reported as "damaged",
  because the hand-built app bundle wasn't signed as a whole; it's now
  ad-hoc signed (see below), which still needs confirming on a Mac. Certificate
  trust (via the `security` CLI, into the login keychain, behind
  CLeARINET's own confirmation dialog since `security` has no OS-level
  install prompt the way Windows does) and system proxy registration (via
  `networksetup`, per network service) are both implemented — see the
  Interception Certificate Design doc's "Platform status" section for the
  full design, including one real open question this session couldn't
  resolve: whether `networksetup` needs admin elevation at all. Nothing
  here has run against a real Mac from this session; `ci.yml`'s own test
  matrix does run on `macos-latest`, but the tests themselves are
  deliberately pure-logic-only (argument/output parsing, no real `security`/
  `networksetup` invocation) rather than live integration tests.
  `.github/workflows/release-macos.yml` builds an installable `.dmg` from
  every tagged release the same way `release-windows.yml` builds the
  Windows one. It's only **ad-hoc signed**, not Developer ID signed or
  notarized (no Apple Developer account available to this project), so
  macOS asks you to allow it once, in System Settings > Privacy & Security;
  see `installer/macos/build-installer.sh`'s own remarks.
- **Only some settings persist yet.** The port choice, Tools-menu panel
  toggles, filter text, FiddlerScript path and legacy-host auto-launch are
  remembered between runs. Window size and position, AutoResponder rules
  and a Preferences/`about:config` editor aren't there yet. Breakpoints
  and the AutoResponder's on/off switch deliberately always start off. See
  the Preferences Design doc.
- **No built-in HAR or Chromium NetLog import.** NetLog import works
  through Eric Lawrence's ported NetLog importer extension (see above),
  an optional extension in the installers. No explicit HTTP/2 or TLS
  1.3 handling yet.
- **The main app's own compiled-extension support is source-level, not
  binary.** An already-compiled Fiddler Classic extension `.dll` can't be
  dropped straight into CLeARINET's own Extensions folder as-is; a new
  extension has to be built against CLeARINET's own (source-level
  compatible) interfaces. Real binary compatibility does exist, just not
  here — see `tools/Clearinet.LegacyExtensionHost`, the separate, optional
  legacy extension host above, which runs real unmodified-*source*
  extensions (recompiled against its own compat assembly) against real
  proxied traffic; it's not part of the main app's own build, but the
  Windows installer can include it as an unchecked-by-default optional
  component.
- **No headless/CLI mode.** Left out of this milestone by design, not by
  oversight.

## Relationship to Fiddler

CLeARINET is built independently: from publicly available documentation,
publicly observable behavior (such as the structure of `.saz` files
produced by the real tool), and original design decisions — never from
decompiled or leaked Fiddler source. See [CONTRIBUTING.md](CONTRIBUTING.md)
for the full clean-room policy that governs every contribution here.

This project is also intended, eventually, to be contributed back to
Eric Lawrence's [github.com/ericlaw1979/Clearinet](https://github.com/ericlaw1979/Clearinet).

## License

MIT — see [LICENSE](LICENSE).
