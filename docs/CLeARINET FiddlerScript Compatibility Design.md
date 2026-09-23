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
  immutable records — written, but **not called by anything yet**: this
  pass stops at running a handler and letting a caller inspect its
  effects on the `Exchange`, not at wiring those effects into
  `InterceptingProxyListener`'s actual request/response flow. That's the
  next slice (Phase A2 below), deliberately kept separate so the pure
  scripting engine could be reviewed and tested on its own first — the
  same staged approach this project already used for AutoResponder (core
  engine + tests, then listener integration as a distinct step).
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
real FiddlerScript). As with every other pass this project, none of this
has been run through an actual `dotnet build`/`dotnet test` yet — that
happens on your machine/CI, the same as every other feature so far.

## What's not built yet — the rest of the "everything" scope

Given in priority order, each a real, separately-sized slice:

1. **Phase A2 — listener integration.** Wire `FiddlerScriptHost` into
   `InterceptingProxyListener`'s actual request/response pump: build an
   `Exchange` per request, call the handlers at the right points
   (`OnPeekAtResponseHeaders` before the body streams in,
   `OnBeforeResponse` once it's fully read), and actually apply
   `ToRequest()`/`ToResponse()`'s results — plus decide how
   `x-breakrequest`/`x-breakresponse` fold into `BreakpointManager` (the
   same "fold into the existing fork" pattern AutoResponder's
   `*bpu`/`*bpafter` already established) and how `bypassGateway`/
   `oSession.hostname` retargeting affects the lazy upstream connection.
2. **Phase B — Rules-menu preferences.** `RulesOption`/`RulesString`/
   `BindPref` — checkable Rules-menu items backed by script-visible
   booleans/strings. Needs a new "Rules menu" surface in the desktop app;
   the preprocessor already strips these attribute blocks today so a
   script with them still loads, but nothing reads what they declare yet.
3. **Phase C — Context Actions and Tools-menu commands.** `ContextAction`
   (right-click menu items operating on selected sessions) and
   `ToolsAction` (Tools-menu commands) — new Avalonia context-menu and
   Tools-menu hooks, independent of the scripting engine itself.
4. **Phase D — custom grid columns.** `BindUIColumn` — a script-computed
   column in the session `DataGrid`, recomputed per row.

None of B/C/D are blocked on each other or on Phase A2 specifically
(each is its own UI surface), but all of them need *some* working script
host to have anything to bind to, which is why A came first.
