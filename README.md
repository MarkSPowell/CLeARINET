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
- On Windows, registers itself as the system proxy (WinINET) while
  running, and recovers cleanly if a previous run didn't shut down
  properly (crash, kill, unclean shutdown).
- A session list with live filtering: free-text search across the URL
  and headers (never body content — see the filter grammar's own remarks
  on why), plus `method:`, `host:`, and `status:` query tokens (exact,
  class like `4xx`, comparison, and range forms).
- Request/response inspector tabs: Headers, Raw (decoded text, with
  automatic decompression), and Hex.
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
- A **Tools** menu of checkable toggles that show or hide the
  FiddlerScript, Extensions, breakpoint-condition, and AutoResponder
  panels on the main screen, so only what you're actually using takes up
  space.
- Export captured sessions to a `.saz` (Session Archive Zip) file, or
  import a previously saved one back in.
- An automatic or manually-specified listening port (defaults to Auto).
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

The first time you click Start, Windows will prompt you to trust a new
local root certificate (named `DO_NOT_TRUST_ClearinetRoot...`, following
Fiddler Classic's own naming convention for the same purpose) — this is
what lets CLeARINET see inside HTTPS traffic on this machine. Nothing
captured ever leaves the device on its own.

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
  plan and tenets, the interception certificate design, the FiddlerScript
  compatibility design, the .NET extension compatibility design, the
  Fiddler feature/tier inventory, and breakpoints research notes.
- `src/Clearinet.ProxyCore` — the proxy engine: the listener, HTTP
  message parsing, sessions, breakpoints, AutoResponder, certificates,
  SAZ export, and Windows system-proxy registration.
- `src/Clearinet.Extensibility` — the inspector contract and the built-in
  inspectors (Headers, Raw, Hex).
- `src/Clearinet.Compatibility` — FiddlerScript compatibility
  (`FiddlerScriptRunner`, the `Exchange`/`AppObject` shim, the directive
  scanner behind the Rules menu/Script Actions/custom columns) and
  compiled .NET extension compatibility (`ExtensionHost` and the
  `IFiddlerExtension`-family interfaces) — see both design docs above for
  what's built and what's still scoped out.
- `apps/Clearinet.DesktopUi` — the Avalonia desktop UI.
- `tools/Clearinet.DevHost` — a console host used during early
  development of the proxy core.
- `tools/Clearinet.SampleExtension` — a small, separately-compiled .NET
  extension used to validate `ExtensionHost` against a real `.dll` rather
  than only in-process fakes; see its own README for what it demonstrates
  and how to build it.
- `tests/` — unit tests for the projects above.

## Known limitations

Listed here on purpose, rather than left for someone to discover the hard
way:

- **Windows only, for now.** Only the Windows certificate trust-store
  path is implemented — macOS trust-store installation hasn't been built
  yet, even though every project targets plain `net10.0` and the UI
  itself (Avalonia) already runs on macOS.
- **No app-level settings persistence.** Port choice, which Tools-menu
  panels are checked, breakpoint conditions, and filter text all reset to
  their defaults on every run — nothing about the app's own UI state is
  saved between sessions yet. (A loaded FiddlerScript's own `[BindPref]`
  values are a separate, narrower thing and *do* persist, under
  `%LocalAppData%\CLeARINET\` — see the FiddlerScript Compatibility Design
  doc.)
- **No HAR or Chromium Netlog import**, and no explicit HTTP/2 or TLS 1.3
  handling yet.
- **No compiled-extension binary-compatibility shim.** An already-compiled
  Fiddler Classic extension `.dll` can't be loaded as-is; a new extension
  has to be built against CLeARINET's own (source-level compatible)
  interfaces. See the .NET Extension Compatibility Design doc.
- **No import/export format picker.** With more than one loaded extension
  proffering session import or export, File > Import/Export via Extension
  always uses the first one found rather than letting you choose.
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
