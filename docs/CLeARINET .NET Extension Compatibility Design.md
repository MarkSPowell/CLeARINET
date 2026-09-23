# CLeARINET .NET Extension Compatibility Design

Detailed design for the `Clearinet.Compatibility.Extensions` namespace,
sibling to `Clearinet.Compatibility.FiddlerScript` (see that design doc).
Read the Project Plan's "Fiddler Classic compatibility review" section
first for the business context (Eric Lawrence's feedback, the decision to
build FiddlerScript first and defer compiled extensions) and this doc's
own "Decision: FiddlerScript first" paragraph, which this work picks back
up.

Built from public documentation only —
[fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp](https://fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp),
fetched directly for this pass and quoted verbatim below where it matters
— consistent with this project's clean-room policy. No implementation code
is reproduced from any compiled extension's decompiled IL anywhere in this
codebase.

## Binary compatibility vs. source compatibility — the central finding

This project has a copy of a real, closed-source, compiled Fiddler
Classic add-on (`SAZClipboard.dll`, in `Local Resources/`) on hand. Rather
than decompile it — which would cross the clean-room line, since no public
source was known to exist for it at the time — it was inspected with
plain string/PE-metadata tools (`file`, `strings`, a raw PE-header read):
confirmed to be a real .NET Framework 2.0 assembly that references
`Fiddler`, `FiddlerApplication`, and `IFiddlerExtension` directly. That's
metadata inspection, not decompilation — no method body/IL was ever
disassembled or read.

That one fact settles an important question: **an existing compiled
Fiddler Classic extension .dll cannot be loaded by a CLeARINET-defined
interface, no matter how exactly its member shape matches Fiddler's own.**
.NET requires the exact same assembly-qualified type identity (namespace +
name + defining assembly) for a class to be recognized as implementing a
given interface — a structurally-identical-but-separately-defined
`Clearinet.Compatibility.Extensions.IFiddlerExtension` is not the same
type as `Fiddler.IFiddlerExtension`, and reflection-based discovery will
never bind the two. This isn't a research gap further docs would resolve;
it's how the CLR works.

This project's own interfaces (below) are therefore **source-level
compatible, not binary-level compatible**: an engineer with an existing
extension's *source* can port it here with a new `using`/base-interface
and a recompile — matching Eric Lawrence's own stated need ("easily
ported with minimal effort," which presupposes having the source) — but
an already-compiled `.dll` like `SAZClipboard.dll` can't be dropped in
unmodified.

**A theoretical path to real binary compatibility exists and is
explicitly NOT pursued in this pass:** a custom `AssemblyLoadContext`
with a `Resolving` handler could intercept a loaded extension's reference
to the real `Fiddler` assembly and substitute an in-memory, clean-room
shim assembly (same namespace/type names, independently authored from
public docs) that adapts into CLeARINET's own engine underneath.
Interesting evidence found while scoping this: `SAZClipboard.dll`'s
`Fiddler` assembly reference doesn't carry a visible strong-name token in
its metadata strings (unlike its `mscorlib`/`System.Drawing` references),
suggesting Fiddler's own core assembly may not be strong-named, which
would remove one binding obstacle. But this is left as a flagged future
option, not a plan: it raises real questions (trademark use of the
`Fiddler` name/namespace even in an interoperability shim; whether
"structurally compatible so old binaries bind to it" reads as
independent reimplementation or as something closer to redistributing
Fiddler's own API surface) this project isn't positioned to resolve on
its own. Eric Lawrence — the original author of Fiddler, and already
directly engaged with this project — is the right person to ask before
any of this is built.

**Also found while scoping this:** a Creative Commons (BY-SA 3.0)
copy of `SAZClipboard`'s actual source
([fiddler.wikidot.com/sazclipboard](https://fiddler.wikidot.com/sazclipboard),
possibly old/out of date) exists. Useful as read-only reference for
understanding real extension behavior — but CC BY-SA's share-alike terms
don't obviously mix with this project's own plain MIT license (Creative
Commons itself advises against using CC licenses for software), so
nothing in this codebase is adapted from it. Worth a licensing decision
from the project owner, not something to route around quietly.

## The interfaces: reproduced member-for-member, `Exchange` as the shared session object

`src/Clearinet.Compatibility/Extensions/`:

- **`IFiddlerExtension`** — `void OnLoad()`, `void OnBeforeUnload()`.
- **`IAutoTamper : IFiddlerExtension`** — `AutoTamperRequestBefore`/
  `AutoTamperRequestAfter`/`AutoTamperResponseBefore`/
  `AutoTamperResponseAfter`/`OnBeforeReturningError`, each
  `(Exchange oSession)`. The compiled-extension equivalent of
  FiddlerScript's `Handlers.OnBeforeRequest`/`OnBeforeResponse`, with two
  extra hook points (`*After`) FiddlerScript's own two-handler surface
  doesn't expose.
- **`IAutoTamper2 : IAutoTamper`** — adds `OnPeekAtResponseHeaders(Exchange oSession)`.
- **`IAutoTamper3 : IAutoTamper2`** — adds `OnPeekAtRequestHeaders(Exchange oSession)`.
- **`IHandleExecAction`** — standalone, `bool OnExecAction(string sCommand)`.
  The public docs give the signature but not a spelled-out return-value
  contract; this project treats `true` as "handled, stop looking" as a
  flagged, reasonable-but-unconfirmed assumption.

Every method signature above is reproduced verbatim from
fiddlerbook.com/fiddler/dev/IFiddlerExtension.asp, fetched directly for
this pass.

**Deliberate choice: these interfaces take
[`Exchange`](../src/Clearinet.Compatibility/FiddlerScript/Exchange.cs) —
the exact same session-facing object FiddlerScript's `Handlers` functions
already use — rather than a second, parallel "Session-shaped" type built
just for .NET extensions.** One object model serves both compatibility
layers: an extension author already familiar with Fiddler's
`oSession.hostname`/`oSession.oRequest`/`oSession["ui-color"]`/etc. finds
the exact same names here, and CLeARINET only has one set of
request/response mutation plumbing (`Exchange.ToRequest`/`ToResponse`) to
keep correct, not two that could drift apart. It also means an extension
author doesn't need Jint at all — `Exchange` is a plain public C# class,
usable directly from a compiled extension project via a normal
`ProjectReference`.

## What's built (this pass)

The five interfaces above, plus `tests/Clearinet.Compatibility.Tests/SampleExtension.cs`
(an original class, not a port of `SAZClipboard.dll` or the CC-licensed
source above) implementing every one of them, and
`ExtensionInterfaceTests.cs`, which exercises each method individually
and asserts on its specific, observable effect through `Exchange` — not
just that the interfaces compile. This proves the interface contract
itself works end-to-end through `Exchange`; it does not yet prove
anything about discovery, loading, or real proxy-traffic wiring, all of
which are separate, larger slices below.

## Phase 2 — discovery, listener wiring, inspectors, SAZ hookup (now built)

Everything items 1–4 below used to describe as future work is now built,
end to end: a compiled extension .dll dropped in the right folder is
discovered, gated, loaded into isolation, and its hooks actually run
against real proxied traffic, real inspector tabs, and the File menu —
not just callable directly the way this pass's original
`ExtensionInterfaceTests.cs` exercised the five interfaces alone.

**Discovery/loading** (`Clearinet.Compatibility.Extensions.ExtensionHost`).
Scans one or more folders, non-recursively, for `*.dll` files — matching
the flat, non-recursive `Scripts` folder convention the public docs
describe for Fiddler's own extension folders, just under CLeARINET's own
name (`ExtensionHost.DefaultExtensionsFolder`, not a copy of Fiddler's
path). Each `.dll` loads into its own isolated, collectible
`AssemblyLoadContext` (via `AssemblyDependencyResolver`, so an extension
can ship its own dependency `.dll`s alongside itself), reproducing
Fiddler's own documented `RequiredVersion` gating exactly: an assembly
with no `RequiredVersionAttribute` is silently skipped (not an error —
see the attribute's own remarks), one with an unparseable or too-high
minimum version is skipped with a load error recorded, and one that
passes gets scanned (`ReflectionTypeLoadException`-safe) for concrete,
parameterless-constructible types implementing any of this namespace's
extension interfaces. One instance is constructed per matching type and
registered into every applicable list it qualifies for (a type
implementing both `IAutoTamper` and `IHandleExecAction`, say, lands in
both lists as the same instance) — `AutoTampers`, `RequestInspectors`,
`ResponseInspectors`, `Importers`, `Exporters`, `ExecActionHandlers`, plus
a `LoadErrors` list surfacing everything that went wrong along the way.
`Load()`/`Unload()` call every loaded extension's `OnLoad`/
`OnBeforeUnload` once each, isolating a throw to just that one extension
the same way the rest of this class does. Deliberately scoped as
"load once at startup, unload once at shutdown" — no live hot-reload for
compiled extensions, unlike FiddlerScript's own `Reload()`; real Fiddler
Classic doesn't hot-reload compiled extensions either, so this matches
the thing being reproduced rather than cutting a corner.

**`IAutoTamper` wired into `InterceptingProxyListener`**
(`Clearinet.ProxyCore.Extensions.IExtensionAutoTamperHost`, implemented by
`Clearinet.Compatibility.Extensions.LoadedExtensionSet`). Same
interface-inversion pattern as `IFiddlerScriptRunner`/`FiddlerScriptRunner`
(see that pair's own remarks): `InterceptingProxyListener` takes an
optional `IExtensionAutoTamperHost` and joins its
`HasAnyRequestBeforeHandlers`/`HasAnyResponseBeforeHandlers` into the
exact same body-buffering fork `IFiddlerScriptRunner.HasOnBeforeRequest`/
`HasOnBeforeResponse` already forces, running immediately *after*
FiddlerScript's own handler (an assumed, not confirmed-against-real-Fiddler
ordering — see `IExtensionAutoTamperHost`'s own remarks) and before the
breakpoint check. `LoadedExtensionSet` builds one `Exchange` per call and
runs every loaded extension's hook against that same shared instance in
sequence, so each extension sees the previous one's edits — exactly like
real Fiddler running several loaded extensions' `AutoTamperRequestBefore`
against one shared `oSession`. One extension throwing doesn't stop the
others or the connection. The `*After` pair
(`AutoTamperRequestAfter`/`AutoTamperResponseAfter`) runs unconditionally,
fire-and-observe, right after the corresponding write completes on either
the buffered or relay-live path — it never needs to force buffering, so
it's called from one spot after the two paths rejoin rather than gated
and duplicated inside each. `OnBeforeReturningError` remains explicitly
out of scope, unchanged from item 2 below — there's still no synthetic
error-response code path in the proxy to hang it off.

**`Inspector2`/`IRequestInspector2`/`IResponseInspector2`**
(`Clearinet.Compatibility.Extensions.IInspector2`, adapted by
`Inspector2Adapter` into `Clearinet.Extensibility.Inspection.IInspector`).
As anticipated, this is an adapter into the existing `IInspector`
contract, not a second inspector-hosting system —
`InspectorRegistry.CreateDefault` gained an `IEnumerable<IInspector>?
additional` overload specifically for this, so `InspectorRegistry` itself
stays unaware extensions exist at all. `IInspector2` deliberately omits
real Fiddler's own `AddToTab(TabPage)`/`GetOrder()` — fundamentally
WinForms-coupled, incompatible with this project's "data in, rendering
out" tenet (see `IInspector.cs`'s own remarks) — keeping only what lets a
body-transforming inspector still work: settable `headers`/`body` the
adapter feeds in (headers first, then body, since real inspectors
commonly key off Content-Type), read back out afterward. This is a real,
flagged limitation, not a silent one: with no method that hands back
arbitrary UI content, `Inspector2Adapter.Inspect` can only ever produce
`TextContent` from the resulting bytes (UTF-8 decoded) — an extension that
populated a fully custom WinForms control in real Fiddler has no
equivalent surface here today, consistent with `InspectorContent`'s own
remarks that an image/tree case is future work for every inspector, not
just adapted ones. `GetOrder()`'s absence means every adapted inspector
sorts at a fixed `Inspector2Adapter.DefaultSortOrder`, below the built-ins.

**`ISessionImporter`/`ISessionExporter`**
(`Clearinet.Compatibility.Extensions.ISessionImporter`/`ISessionExporter`,
`ImportedSession`, `ProgressCallbackEventArgs`, `ProfferFormatAttribute`).
`ISessionImporter.ImportSessions` returns `ImportedSession` records — a
CLeARINET-native stand-in shaped exactly like `SessionStore.Add`'s own
parameters, not a freely-constructed `Session` the way real Fiddler's own
importers build one. `Session` itself is technically public-constructible
(an ordinary positional record, nothing stopping `new Session(...)`), but
only `SessionStore.Add` is allowed to hand out real, sequential `Id`s —
it assigns one itself and doesn't even take a pre-built `Session` as a
parameter — so an importer constructing its own `Session` would have to
guess an `Id` that's either going to collide with a real one or be
silently overwritten anyway. `ImportedSession` sidesteps the question
entirely by carrying no `Id` at all. `ISessionExporter.ExportSessions`
uses the real `Session` type directly, since export only ever reads
already-captured sessions (with their real, already-assigned `Id`s) and
never needs to construct one. `ProgressCallbackEventArgs` deliberately drops the separate
`PercentComplete: string` member the public docs mention alongside a
numeric ratio — its exact contract couldn't be pinned down from what's
publicly documented, and a redundant, unverified string duplicate wasn't
worth guessing at; flagged here, not silently dropped. Wired into the
desktop app's File menu as "Import via Extension…"/"Export via
Extension…", each enabled once at least one extension proffers the
matching capability. Deliberately simpler than real Fiddler's own
multi-format Import/Export dialog: with more than one importer/exporter
loaded, or one proffering more than one `[ProfferFormat(...)]`, CLeARINET
always uses the first one found rather than showing a picker — a real
format-choice UI is future work. Neither call ever receives a host-chosen
file path in its `options` dictionary (always passed empty): an importer
or exporter that needs one is expected to gather it itself, the same way
a real Fiddler extension would show its own dialog, rather than this host
inventing an unconfirmed options-dictionary convention.

**Desktop UI wiring.** `MainWindowViewModel` constructs one `ExtensionHost`
pointed at `ExtensionHost.DefaultExtensionsFolder` and calls `Load()` once,
synchronously, in its constructor — no live reload, matching
`ExtensionHost`'s own scope. Its `AutoTampers` are wrapped fresh via
`CreateAutoTamperHost()` and handed to `InterceptingProxyListener` on
every `Start()`, the same way `_fiddlerScriptRunner` already is; its
request/response inspectors are folded into `InspectorRegistry` once,
via the new `CreateDefault(additional)` overload; `Unload()` runs from
`Dispose()`, regardless of whether the proxy happens to be running at the
time, so every loaded extension's `OnBeforeUnload` is still called.

**Extension-status UI panel.** Discovery/loading results were originally
only visible as console output (see `Clearinet.SampleExtension`'s own
README, below) — not usable for anyone running the packaged desktop app
without a visible console attached. `MainWindowViewModel.ExtensionStatus`
(a read-only bound string, `BuildExtensionStatus()`, called once right
after `_extensionHost.Load()`) surfaces the same information directly in
the window instead: one line per folder `ExtensionHost` scanned (whether
it existed, and how many `.dll` files it found there), a summary line with
a per-interface breakdown (`AutoTamper`/request inspector/response
inspector/importer/exporter/exec-action-handler counts, plus a total), and
one line per entry in `ExtensionHost.LoadErrors`. Rendered in `MainWindow.axaml`
as a small read-only "Extensions" status block, the same pattern as the
existing FiddlerScript status panel it sits alongside — deliberately
verbose (every scanned folder, not just a total) rather than a single
summary count, specifically so "did it look in the right place, and what
did it find there" never requires console access at all.

## Validation: `Clearinet.SampleExtension`

`tools/Clearinet.SampleExtension/` is an original, from-scratch sample
extension (not a port of `SAZClipboard.dll` or the CC-licensed source
mentioned above) built specifically to validate everything in the Phase 2
section end to end against a real, separately-compiled `.dll` — the one
thing `LoadedExtensionSetTests.cs`'s in-process fakes can't prove on their
own, since this sandbox has no local `dotnet` build toolchain. One class
per interface (`SampleAutoTamper` implementing the full `IAutoTamper3`
chain, `SampleExecActionHandler`, `SampleRequestInspector`/
`SampleResponseInspector`, `SampleSessionImporter`/`SampleSessionExporter`),
each exercised directly by `tests/Clearinet.SampleExtension.Tests/` (no
desktop app or dropped `.dll` needed for that half), plus the project's own
`README.md` walking through building it, dropping the built `.dll` into
`ExtensionHost.DefaultExtensionsFolder`, and four manual checks against a
running desktop app (discovery/loading, `IAutoTamper` on live traffic,
`Inspector2` tabs, and the Import/Export File-menu commands).

**Confirmed working end to end on a real machine:** running the desktop
app with `Clearinet.SampleExtension.dll` dropped into the extensions
folder showed the Extensions status panel reporting 1 `.dll` found and
loaded (1 `AutoTamper`, 1 request inspector, 1 response inspector, 1
importer, 1 exporter, 1 exec-action handler) — the discovery/gating/
`AssemblyLoadContext` loading path this environment's own test suite
couldn't exercise. This satisfies item 4 under "What's not built yet"
below for discovery/loading specifically; the three `IAutoTamper`/
`Inspector2`/`ISessionImporter`-`ISessionExporter` manual checks in the
README were not separately confirmed back to this session, so they're not
marked done here.

**Tests.** `tests/Clearinet.Compatibility.Tests/LoadedExtensionSetTests.cs`
exercises `LoadedExtensionSet` through its `IExtensionAutoTamperHost`
surface with small, original fake `IAutoTamper` implementations — no real
compiled extension `.dll` anywhere in the suite, matching the
`FiddlerScriptRunner`/`IFiddlerScriptRunner` testability split this one
was built to mirror. `ExtensionHost`'s own disk/reflection/
`AssemblyLoadContext` scanning isn't covered by an automated test in this
pass (there's no local build toolchain in this environment to compile a
throwaway test extension `.dll` against) — validating discovery end to
end against a real compiled extension is one of the manual checks below.

## What's not built yet

1. **The binary-compatibility shim** described above — deliberately not
   started pending Eric Lawrence's input.
2. **A real per-format Import/Export picker UI.** Today's "use the first
   loaded importer/exporter and its first proffered format" is a
   deliberate simplification — see the Phase 2 section above.
3. **`OnBeforeReturningError`.** Still nothing in the proxy to hang it
   off — see the Phase 2 section above for why this is unchanged from
   before.
4. **End-to-end validation against a real compiled extension `.dll`**,
   partially done: `ExtensionHost`'s folder scan, `RequiredVersion` gating,
   and `AssemblyLoadContext` isolation are now confirmed working against
   `Clearinet.SampleExtension.dll` on a real machine (see "Validation:
   `Clearinet.SampleExtension`," above). Still unconfirmed back to this
   session: the three runtime-behavior checks in that project's own
   README — `IAutoTamper` actually mutating live traffic, the `Inspector2`
   tabs rendering in the desktop app, and the Import/Export File-menu
   commands round-tripping sessions.
