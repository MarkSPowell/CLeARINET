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
   ("Headers", "Raw", "Hex") matching Fiddler Classic's own, the
   `DO_NOT_TRUST_ClearinetRoot` certificate-naming convention, and
   AutoResponder's own match/action syntax (`EXACT:`/`regex:`/`NOT:`/
   `METHOD:`, `*redir:`/`*delay:`/`*bpu`/`*bpafter`/etc.) reproduced
   verbatim rather than redesigned.

   **Confirmed and sharpened by Eric Lawrence directly (Sept 2026), on
   reviewing an early build:** *"I do think the long-term direction is
   indeed using a different UI framework and making a lot of improvements
   for modernity. But we have a short term need here at microsoft where
   there's 1000 engineers that have workflows built on the legacy fiddler
   product and extensions and so forth that need to be easily ported with
   minimal effort. Hence my plan to start with something that is very
   compatible with legacy fiddler classic."* This isn't a nice-to-have
   preference — it's a concrete, numbered constraint (1,000 engineers, at
   Microsoft, today) that changes how this tenet should be read: not just
   "familiar to someone who used Fiddler Classic" but "an existing
   Fiddler Classic **workflow or extension** should port with minimal
   effort." See "Fiddler Classic compatibility review" below for what that
   means concretely against the code as it stands today.
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

## Lessons from Fiddler's own history

Eric Lawrence published a retrospective on Fiddler's own mistakes
([textslashplain.com, Nov 2024](https://textslashplain.com/2024/11/24/fiddler-my-mistakes/))
that's worth reading against this codebase directly, not just in spirit.
Checked here against what's actually implemented, not against the design
docs' intentions:

**Already structurally avoided:**

- *Public fields instead of properties, especially raw `byte[]` body
  fields.* Lawrence calls this his worst API mistake — headers and bodies
  exposed as mutable public fields early on, later impossible to convert
  to properties without a breaking change, with `byte[]` bodies over the
  85KB Large Object Heap threshold causing real GC and (on 32-bit)
  address-space-fragmentation pain. `Session`, `CapturedRequest`, and
  `CapturedResponse` (`Session.cs`, `CapturedHttpMessage.cs`) are `sealed
  record`s with init-only properties from day one — there's no field to
  someday need to convert.
- *Thread-per-connection, and the .NET ThreadPool starvation trap he hit
  years later switching away from it* (a 500ms injection delay past 30
  concurrent connections). `InterceptingProxyListener` is async end-to-end
  — `AcceptTcpClientAsync`, `AuthenticateAsServerAsync`,
  `Http1MessageReader`'s reads — never blocks a pool thread waiting on the
  network, so neither failure mode has anywhere to occur.
- *Staying closed-source, which Lawrence calls his biggest regret*
  ("Telerik has allowed Fiddler Classic to stagnate...but we can't
  because the code is closed-source"). MIT plus the intent to upstream
  was chosen specifically to avoid repeating this.
- *Windows-only via a UI framework that couldn't be decoupled later.*
  Avalonia was picked specifically for cross-platform reach from day one
  (see Platform and technology, above) rather than the WinForms lock-in
  that kept Fiddler Classic Windows-only for its whole life. The macOS
  trust-store path isn't built yet, but nothing in the architecture blocks
  it the way WinForms did.
- *Extensibility as an afterthought.* Lawrence calls Fiddler's
  scripting/extension model — born from not wanting to build a filter UI
  — one of its best decisions. `IInspector` and `InspectorRegistry`'s
  isolated-`AssemblyLoadContext` loading are already part of Phase 2, not
  a Phase 3 retrofit.

**Still a live, open risk, same shape as his:**

- *Unbounded in-memory body buffering.* `SessionStore` keeps every
  captured `Session` — including both `CapturedRequest`/`CapturedResponse`
  `Body` byte arrays — in memory for the process's whole life, with no
  eviction; its own doc comment already says "Eviction and on-disk
  spillover for long-running captures are still unsolved." That's the
  same shape as Lawrence's LOH/GC-pressure problem (large bodies — video,
  big downloads — landing as `byte[]` on the heap). 32-bit address-space
  fragmentation isn't a risk here since this only targets 64-bit, but the
  GC-pressure half is open today and gets worse the longer a capture
  session runs. Worth treating "session eviction / body size caps /
  spill-to-disk for large bodies" as a real near-term gap rather than a
  someday item, so it doesn't quietly become load-bearing API surface the
  way the `byte[]` fields did for Fiddler.

**A deliberate, acknowledged tradeoff, not a mistake to fix:**

- *The name `Session`.* Lawrence specifically regrets this name ("there
  are so many different concepts of a `Session` in web networking") and
  says `Exchange` or `Pair` would have been better, but couldn't change it
  without breaking every extension. CLeARINET's core type is also named
  `Session` (`Session.cs`) — inherited on purpose, since tenet 1 is
  API/usage compatibility with Fiddler, and it's presumably what
  ericlaw1979/Clearinet itself calls it too. Noted here so it's an
  explicit, on-the-record choice rather than an accidental repeat.

**Not yet applicable — no verdict to give:**

- *A deliberately deadlock-safe preferences system.* Lawrence calls
  Fiddler's `about:config`-inspired preferences system — built
  specifically to avoid deadlocks in a heavily multithreaded, extensible
  app — one of the few things he's genuinely proud of. CLeARINET has no
  settings persistence yet (see README's Known limitations), so there's
  nothing to get wrong yet — but worth designing with that same
  deadlock-avoidance care in mind when it's eventually built, rather than
  reaching for the obvious approach and finding out later. See
  "Extensible config lists (upstream Issue #2)," below, for a second
  design commitment that same future system should carry.
- *HTTP/2 blocked by `SslStream` never exposing ALPN.* His version of
  .NET's `SslStream` genuinely didn't expose ALPN control, which is why
  "Fiddler Classic still doesn't support HTTP2 to this day." That
  specific platform wall doesn't exist anymore — modern `SslStream`
  exposes `SslServerAuthenticationOptions.ApplicationProtocols` — so
  CLeARINET not doing HTTP/2 yet (already a Known limitation) is a
  scoping decision, not an inherited platform limitation.

## Extensible config lists (upstream Issue #2)

ericlaw1979/Clearinet's [Issue #2](https://github.com/ericlaw1979/Clearinet/issues/2),
"Extensible Lists," states the requirement plainly: "Anything based on a
list of potentially changing data should be based on an overridable
preference," giving
`clearinet.config.processnames.browsers = "msedge.exe;chrome.exe;firefox.exe;brave.exe;iexplore.exe"`
as the example.

This is a design commitment for CLeARINET's eventual Preferences system
(see "Lessons from Fiddler's own history," above, on building it with the
same deadlock-avoidance care Lawrence used) — not something to retrofit
today. Checked against the current codebase, it doesn't actually apply to
anything that exists yet: there's no browser-process list to make
overridable in the first place, since CLeARINET has no per-process capture
feature at all. The one hardcoded "set of X" list that does exist —
`RawTextInspector.KnownUnsupportedEncodings`, the informational
bzip2/compress/sdch map — wouldn't gain real value from being data-driven
either, since decoding a genuinely new encoding needs decoder code, not
just a recognized name.

Recorded here so the commitment isn't lost before Preferences design
actually starts:

- Any future list of "potentially changing data" — process names for a
  later per-process capture feature, a set of hosts to always/never
  intercept, header names an inspector treats specially, and so on —
  should be sourced from an overridable preference from the moment it's
  introduced, not a hardcoded array added "for now."
- Follow Eric's own naming convention (`clearinet.config.<category>.<name>`)
  and semicolon-delimited list values: familiar to anyone coming from
  Fiddler, and already a known, working format rather than one CLeARINET
  would need to invent and document itself.
- The Preferences system itself should treat a config-list value as a
  first-class, typed accessor from day one — parsed once, trimmed,
  case-insensitively deduped — so adding a new overridable list becomes
  "declare a key and a default," not a bespoke parser written per list.

## Fiddler Classic compatibility review (Sept 2026)

Prompted directly by Eric Lawrence's feedback quoted under tenet 1 above:
a pass over what "port an existing Fiddler Classic workflow with minimal
effort" actually requires, checked against what's implemented today
versus what would still block a real Microsoft engineer's existing setup.
Researched from Telerik's own public Fiddler Classic docs
(`telerik.com/fiddler/fiddler-classic/documentation`, sourced from the
[telerik/fiddler-docs](https://github.com/telerik/fiddler-docs) GitHub
repo), Eric Lawrence's own blog, and public example scripts/extensions —
consistent with this project's clean-room policy (no decompiled or leaked
Fiddler source consulted for this review either).

**Already strong, workflow-level compatibility (no porting effort
required today):**
- Session capture, breakpoints (`bpu`/`bpm`/`bps` plus the two "break on
  all" toggles), inspector tab names, SAZ import/export, and AutoResponder
  now all use Fiddler Classic's own naming and (for AutoResponder) its
  exact match/action syntax — someone who knows Fiddler Classic's UI
  already knows this app's UI for these features.
- SAZ files round-trip with the real Fiddler Classic, so existing capture
  archives are portable without any conversion step.

**The real gap, and the one Eric's feedback puts squarely in scope: a
Fiddler Classic *workflow* commonly includes custom automation, not just
UI habits.** Fiddler Classic has two distinct, well-documented
extensibility mechanisms, and CLeARINET currently has a placeholder for
neither:

1. **FiddlerScript (`CustomRules.js`, sometimes `CustomRules.cs`).** A
   single `Handlers` static class with event methods
   (`OnBeforeRequest(Session oSession)`, `OnBeforeResponse`,
   `OnPeekAtResponseHeaders`), written in JScript.NET by default, or in
   C# (Telerik added this as an alternative FiddlerScript language).
   Recompiled automatically whenever `Rules > Customize Rules...` is
   saved. This is documented and, by Eric's own public writing, treated
   as *the* everyday customization path — lighter-weight than a compiled
   extension, and what his own blog posts point readers toward for
   one-off automation. A large share of a script's surface area leans on
   `Session`'s generic string-indexer property bag (`oSession["..."]`,
   e.g. `ui-color`, `x-breakrequest`, `response-trickle-delay`) rather
   than typed members, which is good news for a shim: a
   dictionary-backed indexer on CLeARINET's own session/context type
   would cover a lot of real-world scripts before every named
   `oRequest`/`oResponse`/`util*` member is individually reimplemented.
   ([Understanding FiddlerScript](https://www.telerik.com/blogs/understanding-fiddlerscript);
   [Modify a Request or Response](https://www.telerik.com/fiddler/fiddler-classic/documentation/knowledge-base/fiddlerscript/modifyrequestorresponse);
   [Customize Menus](https://www.telerik.com/fiddler/fiddler-classic/documentation/knowledge-base/fiddlerscript/customizemenus))
2. **Compiled .NET extensions.** DLLs implementing `IFiddlerExtension`
   (`OnLoad`/`OnBeforeUnload`), optionally `IAutoTamper`/`IAutoTamper2`/
   `IAutoTamper3` (request/response tampering hooks, explicitly documented
   as firing on background threads), `Inspector2` plus
   `IRequestInspector2`/`IResponseInspector2` (custom Inspector tabs —
   CLeARINET's own `IInspector` contract, see "Extensibility and core API
   surface" above, is a from-scratch, differently-shaped equivalent, not
   this one), and `ISessionImporter`/`ISessionExporter` (File > Import /
   Export formats). Discovered by folder-scan (a `Scripts` folder under
   Fiddler's install and under the user's Documents) plus reflection —
   every public class needs an assembly-level
   `[Fiddler.RequiredVersion("x.y.z.w")]` attribute or it's silently
   skipped. No MEF or other plugin framework; Telerik's own architecture
   doc states outright that this surface is "subject to change," i.e. it
   was never a versioned, contractual API in the first place — which
   works in CLeARINET's favor, since there's no exhaustive frozen surface
   to match, only the commonly-used members.
   ([Implement Interfaces](https://www.telerik.com/fiddler/fiddler-classic/documentation/extend-fiddler/interfaces);
   [Extend with .NET](https://www.telerik.com/fiddler/fiddler-classic/documentation/extend-fiddler/extendwithdotnet);
   [Build a Custom Inspector](https://www.telerik.com/fiddler/fiddler-classic/documentation/extend-fiddler/custominspector);
   [Importer/Exporter Interfaces](https://www.telerik.com/fiddler/fiddler-classic/documentation/extend-fiddler/importerexporterinterfaces);
   [Fiddler Classic Architecture Info](https://www.telerik.com/fiddler/fiddler-classic/documentation/knowledge-base/fiddlerarchitecture))

Neither mechanism has a published usage survey, but the circumstantial
evidence (Eric's own posts, the shape of Telerik's public Add-ons
gallery) points to FiddlerScript being the higher-volume, lower-friction
mechanism day to day, with compiled extensions reserved for
redistributable tools or capabilities script can't reach (new Inspector
tabs, import/export formats). For "1,000 engineers with existing
workflows," FiddlerScript compatibility is very likely the higher-value
target of the two if only one can be built first.

**What this means for `Clearinet.Compatibility` (currently an empty
Phase 3 placeholder, per "Platform and technology" above):** the honest
read is that Phase 3 is too late for this constraint as originally
scoped — Eric's feedback reframes FiddlerScript/extension compatibility
as closer to an MVP-level concern for the intended Microsoft rollout than
a Beta nice-to-have.

**Decision: FiddlerScript first.** Of the two mechanisms above, you chose
FiddlerScript compatibility as the higher-priority target — consistent
with the research above (it's the everyday, lower-friction mechanism;
compiled `.NET` extensions are the heavier, less-common path). Compiled
extension support (`IFiddlerExtension`/`IAutoTamper*`/`Inspector2`/
`ISessionImporter`/`ISessionExporter`) stays a real future target but is
explicitly deferred, not abandoned.

**A load-bearing technical constraint discovered while scoping this,
before any implementation started:** `Microsoft.JScript` (the JScript.NET
compiler Fiddler Classic's default `CustomRules.js` is written against)
was never ported to .NET Core/.NET 5+ and isn't available on .NET 10 —
confirmed by an open, unresolved .NET runtime team feature request asking
for it ([dotnet/runtime#27155](https://github.com/dotnet/runtime/issues/27155))
and by JScript.NET's own history as a .NET-Framework-only technology
([Wikipedia: JScript .NET](https://en.wikipedia.org/wiki/JScript_.NET)).
This means CLeARINET cannot literally execute an existing `CustomRules.js`
file's JScript.NET the way Fiddler Classic itself does — a shim has to
substitute a different engine underneath the same `Handlers`-class
surface, not just port the compiler forward. Two candidate engines,
usable together rather than as an either/or:
- **[Jint](https://github.com/sebastienros/jint)** — a mature, actively
  maintained, pure-C# ECMAScript interpreter with straightforward .NET
  object interop, for running the `CustomRules.js` (JavaScript) variant.
  The gap to flag honestly: Jint targets standard ECMAScript, and
  JScript.NET has a handful of non-standard extensions (chiefly typed
  variable declarations like `var x : String = "";`) that plain
  ECMAScript doesn't have — real-world scripts using those would need a
  small preprocessing pass (strip the `: Type` annotations) before Jint
  can parse them, not a fundamental blocker but a real edge to handle,
  not hand-wave past.
- **Roslyn scripting** (`Microsoft.CodeAnalysis.CSharp.Scripting`,
  already a first-class, actively maintained part of the .NET ecosystem)
  for the `CustomRules.cs` (C#) FiddlerScript variant Telerik later added
  — a much more direct port since it's real C# already, no interpreter
  compatibility gap to manage.

Concretely, both engines would compile/interpret user script text against
the same shimmed `Session`/`FiddlerObject` surface described above, so
supporting both isn't double the design work, just two front-ends onto
one shim.

**Implementation has started** — see the dedicated
**[CLeARINET FiddlerScript Compatibility Design](CLeARINET%20FiddlerScript%20Compatibility%20Design.md)**
doc for the full detail. Summary: Jint is the engine (Roslyn scripting for
the C# variant is planned, not yet built); the script-facing type is
named `Exchange`/`AppObject` (matching `ericlaw1979/Clearinet`'s own
already-published sample, once that was actually checked rather than
presumed — see `Session.cs`'s own corrected doc comment) while every
member keeps Fiddler Classic's exact original casing, since that's what
an *existing* `CustomRules.js` is actually written against. The engine,
shim types, and JScript.NET-to-ECMAScript preprocessor are built and
unit-tested (`src/Clearinet.Compatibility/FiddlerScript/`,
`tests/Clearinet.Compatibility.Tests/`); wiring the result into
`InterceptingProxyListener`'s real request/response flow, plus the
Rules-menu/Context-Action/Tools-menu/custom-column UI surfaces the
"everything" scope decision above covers, are the remaining, separately
staged phases the design doc lays out.

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
