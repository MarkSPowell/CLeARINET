# Fiddler Feature Inventory

Referenced from `IInspector.cs` and `InspectorRegistry.cs` as the place
that tiers Fiddler Classic's features against CLeARINET's own roadmap
(MVP / Beta / 1.0 / out of scope) — but, like the other docs in this
folder, it was never actually written down anywhere accessible. This is
a genuinely thin reconstruction: only two rows are grounded in an actual
code reference. Everything else below the line is scaffolding for you to
fill in, not a real inventory yet — treat the table structure as the
useful part of this file for now, not its contents.

## Grounded (directly referenced in code)

| Feature | Tier | Source |
|---|---|---|
| Custom inspectors (third-party inspector plugins) | Beta | `IInspector.cs`, `InspectorRegistry.cs` |
| Extension loading (Inspectors-folder assembly loading into isolated `AssemblyLoadContext`s) | Beta | `InspectorRegistry.cs` |

## Shipped today (inferred from what's actually implemented, not from a tiering decision)

These aren't "tiered" anywhere — they're just what exists right now, listed
here so the table has a real MVP column to compare against once the rest
of this doc is filled in.

| Feature | Status |
|---|---|
| HTTPS interception (proxy + per-install root CA + per-host leaf certs) | Shipped, Windows only |
| Session capture and live list | Shipped |
| Breakpoints: break on all requests / break on all responses | Shipped (UI-exposed) |
| Breakpoints: URL-contains, method-equals, status-equals rules (Fiddler's `bpu`/`bpm`/`bps`) | Implemented in `BreakpointRules`, **not yet exposed in the UI** — only the two "break on all" toggles are wired up in `MainWindowViewModel` |
| SAZ export | Shipped |
| SAZ import | Shipped |
| Headers / Raw / Hex inspectors | Shipped |
| Response decompression (gzip, deflate, zstd, chains) | Shipped |
| bzip2 / `compress` encodings | Deliberately not supported |
| SDCH | Recognized, not decoded |
| Session filtering (DevTools-style `method:`/`host:`/`status:` query grammar) | Shipped — new relative to Fiddler Classic, per tenet 3 |
| System proxy auto-registration (WinINET) with crash recovery | Shipped, Windows only |
| FiddlerScript / rules execution | In progress: script engine (Jint), `Exchange`/`AppObject` shim, and JScript.NET-to-ECMAScript preprocessor built and unit-tested; not yet wired into `InterceptingProxyListener`, and Rules-menu/Context-Action/Tools-menu/custom-column UI surfaces not started -- see the FiddlerScript Compatibility Design doc |
| HAR import | Not shipped |
| Chromium Netlog import | Not shipped |
| Traffic replay / Autorespond | Shipped — full Fiddler Classic AutoResponder syntax (`EXACT:`/`regex:`/`NOT:`/`METHOD:` matches; `*redir:`/`*delay:`/`*header:`/`*flag:`/`*reset`/`*drop`/`*CORSPreflightAllow`/`*exit`/`*bpu`/`*bpafter` actions; serve-file and fetch-URL actions), see `AutoResponderRules` |
| Explicit HTTP/2 or TLS 1.3 handling | Not shipped |
| Headless / CLI mode | Out of scope for the MVP by design |

> **Needs your input:** the actual point of this document — going
> feature-by-feature through what Fiddler Classic offers (its menus,
> Inspectors, Rules, AutoResponder, the QuickExec command set, Composer,
> Statistics) and assigning each one a tier. That's knowledge only you
> have; I only know what's already been built or explicitly discussed in
> this conversation. Once this is filled in, the "Custom inspectors" and
> "Extension loading" rows above should move down into wherever they
> naturally sort in the fuller table, rather than staying a special
> two-row section.
