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
  responses, inspect and edit a paused message's raw text, then Resume or
  Abort. The breakpoints panel only takes up space once it's actually
  needed.
- Export captured sessions to a `.saz` (Session Archive Zip) file.
- An automatic or manually-specified listening port.

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

## Project layout

- `src/Clearinet.ProxyCore` — the proxy engine: the listener, HTTP
  message parsing, sessions, breakpoints, certificates, SAZ export, and
  Windows system-proxy registration.
- `src/Clearinet.Extensibility` — the inspector contract and the built-in
  inspectors (Headers, Raw, Hex).
- `src/Clearinet.Compatibility` — a placeholder for a FiddlerCore-shaped
  compatibility adapter, planned for a later phase; intentionally empty
  today (see its own doc comment).
- `apps/Clearinet.DesktopUi` — the Avalonia desktop UI.
- `tools/Clearinet.DevHost` — a console host used during early
  development of the proxy core.
- `tests/` — unit tests for the projects above.

## Known limitations

Listed here on purpose, rather than left for someone to discover the hard
way:

- **Windows only, for now.** Only the Windows certificate trust-store
  path is implemented — macOS trust-store installation hasn't been built
  yet, even though every project targets plain `net10.0` and the UI
  itself (Avalonia) already runs on macOS.
- **No settings persistence.** Port choice, breakpoint checkboxes, and
  filter text all reset to their defaults on every run — nothing is
  saved between sessions yet.
- **No HAR or Chromium Netlog import**, no traffic replay/autorespond,
  and no explicit HTTP/2 or TLS 1.3 handling yet.
- **No FiddlerScript-compatible rules or FiddlerCore-shaped API surface
  yet.** `Clearinet.Compatibility` is a placeholder for that; real design
  work on it is planned for a later phase, not this one.
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
