# CLeARINET FiddlerScript Compatibility Design

Detailed design for the `Clearinet.Compatibility` FiddlerScript engine,
split out from the Project Plan's "Fiddler Classic compatibility review"
section once there was enough here to warrant its own doc. Read that
section first for the business context (Eric Lawrence's direct feedback,
the decision to target FiddlerScript before compiled `.NET` extensions)
and the "everything, including Context Actions/Tools/custom columns"
scope decision this doc plans against.

Built from public documentation only — Telerik's own Fiddler Classic docs,
`ericlaw1979/Clearinet`'s public `Content/SampleRules.js`, and
fiddlerbook.com's public cookbook — consistent with this project's
clean-room policy. No script text is reproduced verbatim from either
source anywhere in this codebase; every test script in
`tests/Clearinet.Compatibility.Tests` was written fresh for this project,
styled the same way real scripts are but not copied from one.

## Engine: Jint, not JScript.NET

Fiddler Classic's own `CustomRules.js` is compiled with `Microsoft.JScript`
(JScript.NET). That compiler was never ported past .NET Framework and
isn't available on .NET 10 — there's an open, unresolved .NET runtime team
feature request asking for it
([dotnet/runtime#27155](https://github.com/dotnet/runtime/issues/27155)).
There's no way to run an existing script's literal JScript.NET here; a
different engine has to stand in underneath the same script-facing
surface.

**[Jint](https://github.com/sebastienros/jint)** (v4.16.3 as of this
writing) is that engine: a mature, actively maintained, pure-C#
ECMAScript interpreter, targeting `net10.0` directly, with CLR interop
(`Options.AllowClr()`) and support through ES2022 (including static class
fields/methods) — see `Clearinet.Compatibility.csproj`'s own comment on
the package reference.

**Roslyn scripting** (`Microsoft.CodeAnalysis.CSharp.Scripting`) remains
the planned engine for the *C#* FiddlerScript variant Telerik later added
(`CustomRules.cs`) — genuinely valid C#, so no preprocessing gap to manage
the way JScript.NET's JS-flavored syntax has. **Not implemented in this
pass.** Real-world samples fetched while scoping this skew toward the
JS/JScript.NET variant (Eric Lawrence's own blog treats FiddlerScript's
JS form as the default, everyday path — see the Project Plan's
compatibility review), so that's the higher-value first target; C#
support is a clean, additive follow-on once the JS path is proven out
against real scripts.

## Naming: `Exchange`/`AppObject` as types, Fiddler Classic's own names as members

`ericlaw1979/Clearinet`'s own public `Content/SampleRules.js` has already
moved on from Fiddler Classic's `Session`/`FiddlerObject` naming to
`Exchange`/`AppObject` (`static function OnBeforeRequest(oEx: Exchange)`),
noting directly in its own comments that "in the SAZ format, the term
'Exchange' is written as 'Session'." This codebase's own `Session.cs` had
previously *guessed* that ericlaw1979/Clearinet "presumably" kept calling
it `Session` too — now confirmed wrong, and corrected there.

This project follows `Exchange`/`AppObject` for the **type names**
(`Clearinet.Compatibility.FiddlerScript.Exchange`/`AppObject`) — free to
do, since a FiddlerScript type annotation like `oSession: Session` is
erased entirely by the preprocessor before Jint ever sees it (script code
never constructs one of these or references the .NET type name, only
whatever parameter name the script itself chose).

The **member surface** is the opposite call, deliberately: every property
and method keeps Fiddler Classic's own exact casing (`hostname`, not
`Hostname`; `responseCode`, not `ResponseCode`) rather than
ericlaw1979/Clearinet's own partially-renamed set (`host`/`urlContains()`
in place of `hostname`/`uriContains()`, per that repo's sample). The
actual porting target — per Eric's own stated business need — is an
*existing* `CustomRules.js` a Microsoft engineer already has, written
against Fiddler Classic's real names, not against a brand-new sample file.
Aliasing ericlaw1979/Clearinet's newer member names too, once/if that
repo's convention solidifies, is future work, not done here.

## What's built (this pass)

`src/Clearinet.Compatibility/FiddlerScript/`:

- **`Exchange`/`ExchangeRequest`/`ExchangeResponse`/`ExchangeHeaders`/`ExchangeFlags`** —
  the script-facing object model. Wraps a *mutable* working copy of the
  request/response (`Clearinet.ProxyCore`'s own `CapturedRequest`/
  `CapturedResponse` are immutable records, built for an already-finished
  session) so a handler can actually edit headers/body/routing.
  `ToRequest()`/`ToResponse()` project the working state back into fresh
  immutable records — now actually called, by `FiddlerScriptRunner` below,
  once per handler invocation.
- **`Clearinet.ProxyCore.Scripting.IFiddlerScriptRunner`** (in
  `Clearinet.ProxyCore`, not `Clearinet.Compatibility` — see its own
  remarks for why) and **`FiddlerScriptRunner`** (the implementation, here
  in `Clearinet.Compatibility.FiddlerScript`) — Phase A2, now built; see
  that section below for the full listener-wiring design this pair
  implements.
- **`AppObject`** — Fiddler's `FiddlerObject` helper, thin: `StatusText`,
  `Log.LogString`/`LogFormat`, `alert()` (routed through a host-supplied
  logging callback rather than a real modal dialog, so headless/test use
  doesn't block on a click that will never come), `playSound()` (a
  documented no-op), `ReloadScript()` (a host callback hook). `prompt()`
  and `utilIssueRequest()` (issuing a brand-new outbound HTTP request from
  script) both throw `NotSupportedException` — deliberately, not silently
  no-op, so a script relying on either fails loudly rather than behaving
  as if a prompt were always cancelled.
- **`FiddlerScriptPreprocessor`** — targeted regex passes (not a real
  parser) bridging JScript.NET's non-standard syntax to plain ECMAScript:
  drops `import` lines, strips `[Attribute(...)]` blocks, converts
  `static function Name(...)` to ES2022's `static Name(...)`, strips `:
  Type` annotations from `var` declarations, parameters, and return
  types. See the class's own remarks for the one known gap (an
  object-literal value that happens to look like a bare identifier in
  parameter position can be mis-stripped) — not observed in anything
  fetched while scoping this, flagged rather than hidden.
- **`FiddlerScriptHost`** — loads one script, dispatches
  `Handlers.OnBeforeRequest`/`OnBeforeResponse`/`OnPeekAtResponseHeaders`
  against an `Exchange`. `AllowClr()` is on by default (several real
  cookbook samples reference `System.Text.StringBuilder`,
  `System.Diagnostics.Process`, etc. directly) — an explicit, honest trust
  boundary: unrestricted CLR access is appropriate for a script the
  CLeARINET user wrote for themselves (Fiddler Classic's own trust model
  is identical), not for running a script from somewhere else.
- **`ExchangeCodec`** — backs `utilDecodeResponse()`: gzip/deflate/br,
  deliberately not zstd (would need pulling `ZstdSharp.Port` into this
  project too; a script hitting a zstd-encoded body gets an honest
  `NotSupportedException`, not silent garbage). A deliberate duplication
  of `RawTextInspector`'s own decode logic, not shared code — see that
  class's own remarks on why.

`tests/Clearinet.Compatibility.Tests/` — `FiddlerScriptPreprocessorTests`
(pure string-transform assertions), `ExchangeTests` (pure C#, no Jint
dependency), `FiddlerScriptHostTests` (end-to-end through the real
preprocess-then-Jint pipeline, using original test scripts styled like
real FiddlerScript), and `FiddlerScriptRunnerTests` (Phase A2's own tests
— see that section below). As with every other pass this project, none of
this has been run through an actual `dotnet build`/`dotnet test` yet —
that happens on your machine/CI, the same as every other feature so far.

## Phase A2 — listener integration (now built)

`Clearinet.Compatibility` already references `Clearinet.ProxyCore` (for
`CapturedRequest`/`CapturedResponse`), so `InterceptingProxyListener`
(which lives in `ProxyCore`) can't reference `FiddlerScriptHost`/`Exchange`
(which live in `Compatibility`) directly without a circular project
reference. Solved with interface inversion, the same pattern this codebase
already leans on elsewhere: `Clearinet.ProxyCore.Scripting.IFiddlerScriptRunner`
is a narrow interface defined *in* `ProxyCore` (no Jint dependency at all),
implemented by `Clearinet.Compatibility.FiddlerScript.FiddlerScriptRunner`.
A composition root — today, `MainWindowViewModel`, the same place
`BreakpointManager`/`AutoResponderRules` are already constructed once and
handed to `InterceptingProxyListener.StartOnAvailablePort` — constructs the
real `FiddlerScriptRunner` and hands it to the listener as the interface.

**What the interface looks like:** `HasOnBeforeRequest`/`HasOnBeforeResponse`
(booleans, cached by `FiddlerScriptRunner` at load/reload time from
`FiddlerScriptHost.HasHandler`, so the listener can skip buffering a body
outright when the loaded script doesn't define the relevant handler) and
`RunOnBeforeRequest`/`RunOnBeforeResponse` (each takes a best-effort
session id, the hostname, and the captured request/response, and returns
the edited result). Deliberately **not** returning a force-breakpoint flag
alongside the edited value — an earlier version of this design considered
one, mirroring `AutoResponderOutcome.ForceBreakpointBeforeRequest`, but
nothing downstream would consume it beyond what buffering the body already
implies, so it was left out as dead surface rather than added "just in
case."

**Where it runs, inside `PumpSessionsAsync`:** only on the "forward to the
real server" path — never for a request AutoResponder answers locally (that
branch returns before a script runner is ever consulted). This is a
deliberate scope cut, not a confirmed match for real Fiddler's own ordering
between AutoResponder and FiddlerScript; worth checking against real
Fiddler if that distinction ever matters to you. On the forward path, each
hook point (`HasOnBeforeRequest`/`HasOnBeforeResponse`) joins the *same*
buffer-forcing fork a Rules-based breakpoint or an AutoResponder force-flag
(`*bpu`/`*bpafter`) already drives — buffer the body only when something is
actually going to act on it, relay it live otherwise, exactly as before
this pass. When buffering is forced, the script's own handler runs **first**,
then `BreakpointManager.ApplyRequestBreakpointAsync`/
`ApplyResponseBreakpointAsync` runs against the script's already-edited
value — the same order real Fiddler uses (a script can arm its own pause by
setting `oSession["x-breakrequest"]`/`x-breakresponse` from inside its own
handler), even though nothing here actually reads that flag back yet: see
`BreakpointManager`'s own remarks on the pre-existing gap this mirrors
rather than silently fixes (a `*bpu`/`*bpafter` match, and by extension a
script setting `x-breakrequest`/`x-breakresponse`, doesn't by itself
guarantee an actual UI-visible pause unless a Rules-based condition also
independently matches).

**`SessionStore.PeekNextId()`** backs the "best-effort session id" each
hook point receives as `oSession.id` — `Add()` only assigns a real id once
both the request and the response are known, which for a request-side hook
is still in the future. Explicitly documented as best-effort under
concurrency (another connection's `Add()` can run in between), never a
reservation.

**`FiddlerScriptRunner`** owns the load/reload lifecycle: `LoadFromFile`/
`Reload`/`LoadFromSource`, wired as `FiddlerObject.ReloadScript()`'s own
callback so a script can trigger its own reload. A failed reload (bad
syntax, a runtime error during the script's own top-level code) keeps
whatever script was already running rather than dropping to "nothing
loaded," matching real Fiddler's own "a broken `CustomRules.js` edit
doesn't disable the proxy" behavior; `LoadError` carries the failure for
the UI to show. A handler that throws mid-run is caught the same way —
logged, and the connection falls back to the unedited request/response
rather than taking the connection down. **Not thread-safe** (neither is the
underlying `FiddlerScriptHost`/Jint `Engine`): `InterceptingProxyListener`
pumps one connection per `Task` with no serialization of its own around a
shared `IFiddlerScriptRunner`, so a reload racing a concurrent
`RunOnBeforeRequest`/`RunOnBeforeResponse` call on another connection is a
real, accepted gap in this pass, not silently ignored — in practice this
mirrors how a live AutoResponder rule edit already races the very next
request under this codebase's existing "no formal transaction" concurrency
posture.

**Desktop UI:** a small always-visible "FiddlerScript" panel — a path
TextBox, Load/Reload buttons, and a status line showing which handlers the
loaded script defines (or its load error). No Rules-menu integration yet;
see Phase B below.

**Still deliberately out of scope, even after this pass:**

- The AutoResponder-local-answer scope cut above.
- `x-breakrequest`/`x-breakresponse` not guaranteeing an actual pause (see
  above).
- `OnPeekAtResponseHeaders` stays unwired — nothing calls it from the
  listener yet.
- `Exchange.hostname` retargeting and `Exchange.bypassGateway` stay inert —
  pre-existing gaps in `Exchange` itself (see its own remarks), unchanged
  by this pass; `FiddlerScriptRunner.RunOnBeforeRequest`'s edited request
  never feeds back into which host/port the upstream connection is opened
  against.
- No formal locking around a shared `FiddlerScriptRunner` across concurrent
  connections (see above).

## Phase B/C/D — Rules menu, Context/Tools actions, custom columns (now built)

Built from the same public-documentation sources named at the top of this
doc, specifically Telerik's own "Customize Menus" and "Add Columns to Web
Sessions List" knowledge-base pages plus a Telerik blog post on
`BindPref` — the only public source found describing that one. No script
text reproduced from any of them; every test script in
`tests/Clearinet.Compatibility.Tests`/`tests/Clearinet.SampleExtension.Tests`
is original.

**`FiddlerScriptDirectiveScanner`** (`src/Clearinet.Compatibility/FiddlerScript/`)
scans a script's RAW source — before `FiddlerScriptPreprocessor` strips the
same attribute blocks out entirely — for `RulesOption`, `RulesString`/
`RulesStringValue`, `BindPref`, `ContextAction`, `ToolsAction`, and
`BindUIColumn`. Not a real parser, the same "targeted regex passes" posture
the preprocessor itself takes: every attribute has to sit directly above
the field/method it describes, on its own line(s), with nothing (a
comment, a blank line) wedged in between. A script formatted differently
still loads and runs fine either way (the preprocessor's own stripping
regex is more lenient and doesn't depend on this scanner at all) — it just
won't get a Rules-menu entry/column/etc. for that one declaration. Silent
in that specific case, matching this project's existing "an assembly with
no `RequiredVersion` is silently skipped" precedent for a directive that
was never meant to be found, not one that was found and rejected.

Every scanned attribute becomes a plain data record
(`RulesMenuOption`/`RulesMenuStringOption`+`RulesStringChoice`/
`BindPrefBinding`/`ContextActionDescriptor`/`ToolsActionDescriptor`/
`UIColumnDescriptor`), folded into one `FiddlerScriptDirectives` per loaded
script and exposed as `FiddlerScriptHost.Directives`/
`FiddlerScriptRunner.Directives` (`FiddlerScriptDirectives.Empty` when
nothing's loaded). `FiddlerScriptHost` gained the actual read/write/invoke
surface these bind to — `GetRulesOptionValue`/`SetRulesOptionValue`,
`GetRulesStringValue`/`SetRulesStringValue`, `InvokeContextAction`,
`InvokeToolsAction`, `ComputeUIColumnValue` — each guarded (defense in
depth, the same role `Guard(handlerName)` already played for the three
`Handlers` methods) against a field/method name that isn't actually one of
this instance's own scanned `Directives`.

**Rules menu (`RulesOption`/`RulesString`).** A new, always-present "_Rules"
menu in the desktop app, `MenuItem.ItemsSource`-bound to
`MainWindowViewModel.RulesMenuEntries` — rebuilt from scratch every time a
script (re)loads. Deliberately renders as one **flat** list rather than
real nested Rules-menu submenus: a single flat `ItemsSource` binding is a
well-worn, low-risk Avalonia pattern, where a recursive hierarchical menu
template is not, and this project has no way to build-and-test that
template against a real running app from this sandbox. A grouped entry's
own `RulesMenuOption.SubmenuName`/`RulesMenuStringOption.SubmenuName`
becomes a `"Submenu: Option"` label prefix instead — a readable stand-in
for real nesting, not a fidelity goal, and revisitable once someone can
actually click through the running app. Radio-grouped `RulesOption`s
(`IsRadio` sharing a `SubmenuName`) and `RulesString` choices both clear
their siblings' *underlying script fields*, not just their own checkmarks,
when one is picked — otherwise the script would be left with more than one
of a group's booleans true at once, invisible from the UI but real from the
script's own perspective.

**`BindPref` persistence.** A new `FiddlerScriptPreferenceStore` — a small
JSON file under `%LocalAppData%\CLeARINET\fiddlerscript-prefs.json`, the
same convention `WinInetSystemProxy`'s own crash-safety backup file already
established. Simpler than real Fiddler's own documented "load at script
start, save at script unload" timing: `FiddlerScriptRunner` applies a
persisted value once, at load time, and writes a new value through
immediately on every `SetRulesOptionValue`/`SetRulesStringValue` call —
strictly more crash-safe, not less, and sidesteps this codebase not having
a reliable script-unload event yet. A field carrying `[BindPref]` alone
(no `RulesOption`/`RulesString`, real Fiddler's own `bpResponseURI`
cookbook example is this shape) still gets its persisted value loaded in
at script-load time, but nothing here intercepts further in-script writes
to it — there's no generic way to observe an arbitrary Jint field
assignment without instrumenting every property access. A real, flagged
scope cut, not a silently half-built feature. A preference name containing
`"Ephemeral"` (case-insensitive, matching real Fiddler's own
`fiddlerscript.ephemeral.*` convention) is kept in memory for the owning
`FiddlerScriptPreferenceStore` instance's lifetime but never written to
disk — surviving a script `Reload()`, not a CLeARINET restart, since one
store instance lives exactly as long as its `FiddlerScriptRunner` does.

**Context Actions and Tools-menu commands (Phase C).** A new "_Tools" menu
(`MainWindowViewModel.ToolsMenuEntries`) and a right-click context menu on
the session grid (`ContextActionEntries`, set as the `DataGrid`'s own
`ContextMenu`), both `ItemsSource`-bound the same way the Rules menu is,
rebuilt on every script (re)load. Real Fiddler's own `ContextAction`
operates on every currently-selected session at once (`Session[]`); this
project's grid only supports single selection today, so
`FiddlerScriptRunner.InvokeContextAction` scopes this down to
`SelectedSessionRow` alone — a real, flagged simplification, revisited if
the grid ever grows multi-select. A `ContextAction` is built one
`Exchange` (via `Exchange.ForResponse`, the same factory an in-flight
`OnBeforeResponse` uses) per session, but — unlike `OnBeforeResponse` —
nothing ever calls `ToResponse()` back on the result: an edit a
`ContextAction` makes to a session's headers/body is visible only to the
action's own run, never written back to `SessionStore`. Worth confirming
against a real running app whether Avalonia's `DataGrid` moves
`SelectedItem` on a right-click the way it does on a left-click — if not, a
`ContextAction` ends up operating on whatever was last left-selected, not
necessarily the row actually right-clicked; flagged, not assumed.

**Custom grid columns (Phase D, `BindUIColumn`).** All four of real
Fiddler's documented overloads
(`BindUIColumn(colName)`/`BindUIColumn(colName, bSortNumerically)`/
`BindUIColumn(colName, iColWidth)`/`BindUIColumn(colName, iColWidth, iDisplayOrder)`)
fold into one `UIColumnDescriptor`, told apart by what the scanner's
already-split argument list looks like (a boolean-parsing second argument
is `SortNumerically`, a numeric one is `Width`). `SessionRow` gained a
`ScriptColumns` dictionary, computed **once, at row-creation time**
(`SessionRow.From`, called from the `SessionAdded` handler) via
`FiddlerScriptRunner.ComputeUIColumnValue` — a session captured before a
script (re)loaded a new/changed column keeps whatever `ScriptColumns` held
at capture time; there's no retroactive recompute pass, the same way
editing `OnBeforeResponse` logic doesn't retroactively re-edit
already-captured responses either. `MainWindow.axaml.cs` adds/removes the
actual `DataGridTextColumn`s in code-behind (`DataGrid.Columns` has no
XAML-bindable `ItemsSource` of its own), each bound via a classic indexer
path (`ScriptColumns[Title]`) — the same reflection-binding mechanism the
whole session-grid subtree already opts into via
`x:CompileBindings="False"`, for exactly this reason.
`UIColumnDescriptor.DisplayOrder`/`SortNumerically` are scanned but not
currently honored by the grid (every script column is appended after the
seven built-ins, in declaration order) — flagged in the descriptor's own
remarks rather than silently dropped.

**Still deliberately out of scope, even after this pass:**

- Real nested Rules-menu submenus (flattened to a "Submenu: Option" label
  instead — see above).
- `BindPref`-only fields (no `RulesOption`/`RulesString`) round-trip
  one-way: loaded at script start, never observed on further in-script
  writes.
- Multi-session `ContextAction` (scoped to the single selected session —
  see above).
- `ContextAction` edits to a session's headers/body aren't written back to
  `SessionStore`.
- `UIColumnDescriptor.DisplayOrder`/`SortNumerically` aren't honored by the
  grid yet.
- Whether Avalonia's `DataGrid` moves `SelectedItem` on right-click hasn't
  been confirmed against the real running app.

All of B/C/D needed *some* working script host to have anything to bind
to, which is why A (and A2) came first.
