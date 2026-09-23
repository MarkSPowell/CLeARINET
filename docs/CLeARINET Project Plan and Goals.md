# CLeARINET Project Plan and Goals

The original version of this document was a living doc, written before
any code existed and iterated on outside this repository — it's referenced
throughout the codebase ("the project plan," "tenet 1," "Phase 1's exit
criterion") but was never actually checked in here. This reconstructs what
it settled, gathered from those references plus the decisions actually
made along the way. Sections marked below are genuinely undecided, not
just undocumented — they need your call, not a guess from the code.

## Motivation

CLeARINET exists as a successor to Fiddler Web Debugger (Fiddler
Classic), which Progress Software withdrew in September 2026. Fiddler
Classic was acquired by Telerik in 2012 with the promise it would remain
free forever; after Progress's 2014 acquisition of Telerik and the
original maintainer's 2016 departure, the tool stagnated and Progress
ultimately restricted commercial usage without offering a licensing path
— the "rug pull" CLeARINET is explicitly designed to make impossible for
itself (see `CONTRIBUTING.md`'s no-relicensing commitment).

## Tenets

1. **Be as API/usage-compatible as possible with Fiddler Web Debugger.**
   This shows up throughout the code: `SessionState`'s exact state names,
   the breakpoints model's `bpu`/`bpm`/`bps` naming, inspector tab names
   ("Headers", "Raw", "Hex") matching Fiddler Classic's own, and the
   `DO_NOT_TRUST_ClearinetRoot` certificate-naming convention.
2. **Update for web-standards changes since Fiddler Classic's enhancements
   mostly ended in 2016.** The response-decompression work (zstd support,
   dropping bzip2, SDCH awareness) is a direct instance of this tenet in
   practice, sourced from real gaps identified against the modern web.
3. **Add new features and scenarios based on users' needs**, rather than
   just cloning Fiddler Classic feature-for-feature. DevTools-style session
   filtering (`method:`/`host:`/`status:` query tokens) is an example: a
   grammar closer to what today's users already know from browser dev
   tools than to Fiddler Classic's own QuickExec sigil syntax.
4. **Build a reusable proxy core that other projects and UI front-ends can
   integrate**, not just this one desktop app. This is why
   `Clearinet.ProxyCore` and `Clearinet.Extensibility` have zero
   dependency on Avalonia or any other UI toolkit — `SessionQuery`,
   `BreakpointRules`, and the `IInspector` contract are all plain,
   UI-framework-neutral types for exactly this reason.

## Clean-room policy

Built independently from public documentation (the [Fiddler Classic
docs](https://github.com/telerik/fiddler-docs)), publicly observable
behavior (e.g. the structure of `.saz` files the real tool produces), and
original design decisions — never from decompiled or leaked Fiddler
source. See `CONTRIBUTING.md` for the full policy every contribution here
is held to.

## License

MIT, matching [ericlaw1979/Clearinet](https://github.com/ericlaw1979/Clearinet)
— chosen specifically because CLeARINET is intended to be contributed back
there eventually, not maintained as a permanently separate fork. Paired
with a no-relicensing commitment (see `CONTRIBUTING.md`): the license
won't change later to pull the project behind a paywall.

## Platform and technology

- **Language/runtime**: C# on .NET 10.
- **UI framework**: [Avalonia UI](https://avaloniaui.net/), targeting
  Windows and macOS from one codebase — a different call than
  ericlaw1979/Clearinet's own stated plan (WinForms for the primary
  desktop app, cross-platform UI as a stretch goal), made deliberately for
  cross-platform reach from day one. See the README's note on this for
  context going into any upstream conversation.
- **FiddlerScript compatibility**: handled via a translation layer, not
  by executing FiddlerScript directly. `Clearinet.Compatibility` is the
  placeholder for this — a FiddlerCore-shaped API surface layered on
  `Clearinet.ProxyCore` so existing FiddlerCore integrations port with
  mostly mechanical edits. Scoped to Phase 3 (Beta); intentionally empty
  today.
- **No headless/CLI mode in the MVP.** A deliberate scope cut, not an
  oversight.

## Phases

Reconstructed from phase references scattered across doc comments and
`AllDocumentedStatesArePresent`-style tests. Exact boundaries and whether
a phase is "done" are inferred from what's actually implemented, not from
an authoritative checklist — treat this section as the least certain part
of this reconstruction.

- **Phase 0 — Scaffolding.** Initial project structure and placeholder
  types, including `SessionState`'s Fiddler-Classic-shaped enum, put in
  place before the behavior behind them existed.
- **Phase 1 — HTTPS decryption spike.** Prove out the interception
  certificate design (see the Interception Certificate Design doc) against
  real browser traffic, and get a core test host (`Clearinet.DevHost`)
  capturing a browser's HTTPS traffic and saving a `.saz` file that
  Fiddler Classic itself can open — the literal Phase 1 exit criterion,
  quoted directly in `SazWriter`'s own doc comment. Both are done: the
  proxy core, certificate authority, and SAZ writer all exist and are
  tested.
- **Phase 2 — Desktop UI / MVP.** The real product shell — the Avalonia
  desktop app — replacing DevHost's console output with a live session
  list, plus the features that make it usable day to day: breakpoints,
  filtering/search, response decompression, SAZ export from the UI. This
  is the phase the project is currently in, and the bulk of what's shipped
  so far belongs to it.
- **Phase 3 — Beta.** `Clearinet.Compatibility`'s FiddlerCore-shaped
  adapter, and third-party inspector plugins loaded from an Inspectors
  folder into isolated `AssemblyLoadContext`s (see the Fiddler Feature
  Inventory doc's "Custom inspectors" and "Extension loading" rows, both
  tiered Beta). Not started.

## Extensibility and core API surface

The proxy core (`Clearinet.ProxyCore`) and the inspector contract
(`Clearinet.Extensibility`) are designed to be consumed by more than just
`Clearinet.DesktopUi` — tenet 4. Concretely, this means: no UI-toolkit
types anywhere in either project, plain mutable state (`BreakpointRules`)
rather than `INotifyPropertyChanged` view models, and a closed,
UI-neutral `InspectorContent` shape (`TextContent`/`KeyValueContent`/
`HexContent`/`ErrorContent`) that any host renders however it wants.

## Still undecided

Carried over from the original plan as genuinely open, not just
undocumented:

> **Needs your input**, each of these:
> - Sustainability model (how the project supports itself long-term).
> - Governance and maintainers beyond you.
> - Name/trademark check for "CLeARINET" / "Clearinet" — also worth
>   resolving alongside the branding-stylization question raised during
>   this project's QA pass (README uses "CLeARINET"; ericlaw1979/Clearinet
>   itself uses "ClearINET" as a banner wordmark and "Clearinet" for every
>   product name in body text).
> - Which Fiddler features are the highest-priority targets, and in what
>   order — the Fiddler Feature Inventory doc is where that list should
>   live once it exists.
> - A target date, if one is wanted at all.
