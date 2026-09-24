# Clearinet.LegacyExtensionHost

A standalone, optional, net48 WinForms process for running real compiled
Fiddler Classic extensions that reach into WinForms UI types removed from
.NET 5+ (`MenuItem`, `MainMenu`, `ContextMenu`, `StatusBarPanel`) -- see
`docs/CLeARINET .NET Extension Compatibility Design.md` for the full
background on why this exists as a separate process rather than a feature
of the main CLeARINET app.

**Status: the session bridge is built, and so is the main app's
launch/stop lifecycle for this process** -- a loaded legacy extension's
`IAutoTamper` hooks can run against real traffic captured by CLeARINET's
main proxy, not just the two synthetic demo sessions, and
`apps/Clearinet.DesktopUi` can now start and stop this process itself
(opt-in, off by default) instead of requiring it to already be running.
No open trademark/naming question left on the assembly-identity approach
(see the CompatShim project's own README, "Don't get sued", and the
design doc's "Update -- naming question closed: the second rename") --
the shim no longer presents itself as the real `Fiddler` assembly, or
even carries "Fiddler" in its own name, which means an extension compiled
against the genuine article needs to be recompiled or re-targeted before
it binds here. See "What this proves right now" below for what that means
in practice, "The session bridge" for the IPC layer, and "The launch/stop
lifecycle" for how the main app starts and stops this process.

## What this proves right now

Run the built `.exe`, drop a real compiled Fiddler Classic extension
`.dll` into `Documents\CLeARINET\LegacyExtensions\`, and one of two things
happens:

- **If it's compiled against the real `Fiddler` assembly** (true of every
  sample this project has, unmodified): it's detected as an
  assembly-identity mismatch before this host even attempts to load it --
  a pop-up dialog and a Log-tab entry explain what was expected
  (`Fiddler, Version=...`) versus what this host actually provides
  (`Clearinet.CompatShim, Version=...`), and that it can be recompiled
  against this host's own name if its source is available. The shipped
  dialog is deliberately restrained about *how* to do that beyond
  recompiling -- see `AssemblyMismatch.ToDiagnosticMessage`'s own remarks
  for why it stops short of walking a reader through retargeting a
  compiled binary's own metadata directly. For a developer who does want
  to do that anyway, `..\Retarget-LegacyExtension.ps1` automates the
  `AssemblyRef` edit for the common case (see its own `Get-Help`-style
  comment header) -- `.\Retarget-LegacyExtension.ps1 -DllPath <path to
  the extension .dll>` produces a `<name>.Retargeted.dll` you can drop
  straight into `LegacyExtensions\`. See `AssemblyMismatch` in this
  project and the design doc's "Don't get sued" decision for the full
  reasoning.
- **If it's been recompiled or re-targeted against `Clearinet.CompatShim`
  instead:** it discovers, gates (`RequiredVersion`), loads, and calls
  `OnLoad()` on it for real -- including extensions that reach directly
  into the main window's actual `MenuItem`/`MainMenu`/`ContextMenu`/
  `StatusBarPanel`/`TabControl` controls, which is exactly the thing
  that's impossible in CLeARINET's own .NET 10 process. Two synthetic demo
  sessions are preloaded so `GetSelectedSessions()`-style calls have
  something real to return.

## The session bridge

A named pipe (`Clearinet.LegacyExtensionHost.SessionBridge.v1`,
`SessionBridgeProtocol.PipeName`) that lets CLeARINET's main app call every
loaded legacy extension's `IAutoTamper` hooks against *real* proxied
traffic, live, the same way `LoadedExtensionSet` already does in-process
for source-compatible extensions -- see the design doc's "Session bridge"
section for the full design (wire contract, framing, timeouts, and the
trade-offs behind each of those choices).

- **Started automatically.** `Program.cs` starts `SessionBridgeServer`
  right after `LegacyExtensionLoader.Load()`, regardless of whether
  anything actually loaded -- nothing extra to run.
- **Never a hard dependency for the main app.** CLeARINET's proxy works
  exactly as before whether or not this process is even running. The main
  app's own bridge client (`Clearinet.Compatibility.Extensions.LegacyExtensionHostBridgeClient`)
  probes once, quickly, every time its own proxy is started (Start button)
  -- if this process isn't running, or has zero `IAutoTamper` extensions
  loaded (e.g. every one of the five real samples, unmodified -- see
  above), the probe just comes back empty and every hook becomes a
  no-op passthrough for that run.
- **Launch order:** either start this host yourself before clicking Start
  in the main app, or turn on the main app's own opt-in auto-launch (see
  "The launch/stop lifecycle" below) and let it start this process for
  you. Either way, both sides find each other over the named pipe once
  both are running -- there's nothing to configure beyond having this
  process reachable when the main app's own proxy starts.
- **One pipe connection per hook call**, not one long-lived connection --
  see `LegacyExtensionHostBridgeClient`'s own remarks for why, and
  `SessionBridgeServer.InstanceCount` for the matching choice on this
  side (four concurrent listener instances, not one).

### Testing the session bridge end to end

1. Build both `Clearinet.LegacyExtensionHost.sln` (this folder) and the
   main `CLeARINET.sln`.
2. Put a legacy extension recompiled/re-targeted against `Clearinet.CompatShim`
   (see "Testing against a real extension" above) into
   `Documents\CLeARINET\LegacyExtensions\`, and run
   `Clearinet.LegacyExtensionHost.exe`. Confirm the Log tab shows it
   loaded (no assembly mismatch) and reports `IAutoTamper extension(s)
   loaded` in the `[SessionBridge] Listening on pipe ...` line.
3. Run CLeARINET's main app and click **Start**. The Debug output (or
   Console, depending on how it's launched) should show a
   `[Extension] Session bridge reachable -- N AutoTamper extension(s)
   loaded there.` line.
4. Browse through the proxy as usual. Anything the loaded extension's
   `AutoTamperRequestBefore`/`AutoTamperResponseBefore`/etc. do to a
   session (headers, status code, body -- see `SessionMapping`'s own
   remarks for exactly what round-trips and what doesn't yet, such as
   `HttpVersion`) should show up in CLeARINET's own session view, the
   same as if that extension had been written as a native, in-process one.
5. Stop `Clearinet.LegacyExtensionHost.exe` and click Stop then Start
   again in the main app: extension hooks should silently stop firing
   (fail open, not an error) rather than the proxy breaking.

## The launch/stop lifecycle

`apps/Clearinet.DesktopUi` can start and stop this process itself, so you
don't have to run it by hand every time -- see the design doc's "The
legacy host's launch/stop lifecycle -- built" section for the full design.

- **Opt-in, off by default.** A "Launch Clearinet.LegacyExtensionHost.exe
  automatically on Start" checkbox (Tools -> Legacy Extension Host) drives
  `MainWindowViewModel.AutoLaunchLegacyHost`. Leave it off and nothing
  changes from the manual workflow above.
- **`LegacyExtensionHostLauncher`** (`src/Clearinet.Compatibility/Extensions/`)
  is what actually does it: pings the session bridge first (via
  `LegacyExtensionHostBridgeClient.Ping`) so it never spawns a duplicate
  process if one's already running, then launches
  `Clearinet.LegacyExtensionHost.exe` from a fixed path convention
  (`LegacyExtensionHostLauncher.ExpectedExecutablePath`: a `LegacyHost`
  subfolder next to the main app's own executable) and polls for the pipe
  to come up (up to five seconds, every 250ms) before returning. Wired
  into `MainWindowViewModel.Start`/`Stop`/`Dispose` -- `Dispose` calls
  `StopIfLaunchedByUs` unconditionally so a process this launcher started
  doesn't get orphaned if the app closes before Stop is clicked.
  `StopIfLaunchedByUs` only ever touches a process it itself launched; one
  you started by hand is never closed out from under you.
- **The `LegacyHost` subfolder gets there via a post-build step, not by
  hand.** `Clearinet.LegacyExtensionHost.csproj` has a
  `CopyOutputToDesktopUiLegacyHostFolder` target that copies its own
  build output into `apps/Clearinet.DesktopUi`'s output folder every time
  it builds, so a plain `dotnet build`/`dotnet run`/F5 of the main app
  picks up a populated `LegacyHost` folder automatically (see that
  project's own `csproj` remarks). That covers local dev builds; a real
  installed release gets the same folder a different way -- see
  "Installer packaging" below, no longer a gap.
- **Visible status, not silent.** A "Legacy Extension Host" panel
  (Tools -> Legacy Extension Host to show it) reports what happened --
  already running, launched, not found, or launch failed -- via
  `MainWindowViewModel.LegacyExtensionHostStatus`.

- **Installer packaging.** `.github/workflows/release-windows.yml` builds
  this project separately from the main app's own publish step (plain
  `dotnet build --output publish/legacyhost` -- this is a
  framework-dependent `net48` project, not self-contained), and
  `installer/CLeARINET.iss` offers it as an optional, unchecked-by-default
  `legacyhost` Task -- unchecked to match `AutoLaunchLegacyHost`'s own
  opt-in-off-by-default posture in the app itself, so most installs don't
  carry an extra `net48` payload for a feature they'll never touch.
  Checking it drops the same `LegacyHost` folder the local dev build
  produces into the real install location. Not yet confirmed against a
  real compiled installer on a real machine -- see the design doc's
  "legacy host launch/stop lifecycle" section.

### Still not built

- **No automated tests for the launch/stop lifecycle.**
  `LegacyExtensionHostLauncher` and `LegacyExtensionHostBridgeClient.Ping`
  have no test coverage yet -- everything so far has been manual, hands-on
  verification on a real machine. Process-spawning code is awkward to
  unit test, but at minimum the path-computation logic
  (`ExpectedExecutablePath`) could be pulled out and tested in isolation.
- **Separate window, not merged into CLeARINET's own UI.** This is its
  own top-level window, not reparented into the main Avalonia window --
  a known, accepted limitation (see the design doc's UI-presentation
  discussion), not something silently dropped.
- **Only `IAutoTamper`'s four hooks are bridged.** `Inspector2`,
  `ISessionImporter`/`ISessionExporter`, and `IHandleExecAction` (all
  modeled by the source-compatible layer already, and none referenced by
  any of the five real samples this shim was built against) aren't wired
  across the bridge -- only what the real samples actually use.
- **`HttpVersion` doesn't round-trip through an extension's edits.**
  `Fiddler.Session` has no member for it at all (see `SessionMapping`'s
  own remarks) -- the original value always passes through unchanged,
  which is a fair reflection of what real Fiddler Classic extensions can
  actually see and change through this member surface, not a shortcut
  taken here.
- **A repeated header name (e.g. multiple `Set-Cookie`) collapses to its
  last value** once it passes through `Fiddler.HTTPHeaders`' own
  indexer-based store -- logged (`[SessionBridge] Repeated ... header`),
  not silent, but not fixed either. See `SessionMapping.SetHeaders`'s own
  remarks.

## Building

Needs the .NET Framework 4.8 targeting pack (Developer Pack) installed --
present by default with Visual Studio's usual desktop workloads, likely
already on this machine given the rest of this project is built here.
Deliberately kept in its own solution
(`Clearinet.LegacyExtensionHost.sln`, this folder), separate from the main
`CLeARINET.sln`, so nothing here can affect the main app's build:

```
dotnet build Clearinet.LegacyExtensionHost.sln
```

Or open the `.sln` in Visual Studio directly.

## Testing against a real extension

1. Build.
2. Create `%USERPROFILE%\Documents\CLeARINET\LegacyExtensions\` if it
   doesn't exist.
3. Drop a real compiled Fiddler Classic extension `.dll` in (e.g. one of
   the samples this project inspected -- ask before redistributing any of
   those, though; they're real third-party binaries, not part of this
   repo).
4. Run `Clearinet.LegacyExtensionHost.exe`. An unmodified extension pops a
   dialog explaining the assembly-identity mismatch (see above) rather
   than loading -- that's expected, not a bug. Check the Debug output (or a
   debugger's Output window) for `[LegacyExtensionHost]`-prefixed lines --
   scan results, load errors/mismatches, or silence if nothing qualified
   (no `RequiredVersion` attribute is expected to be silent, matching real
   Fiddler's own documented behavior).
5. Once an extension has been recompiled or re-targeted against
   `Clearinet.CompatShim` (see above): if it adds a menu item to Tools/Rules,
   or shows up in the session context menu, that's the actual
   removed-on-.NET-5+ WinForms surface working for real.
6. Check the **Log** tab in the running window -- scan results, load
   errors/mismatches, and anything the extension or shim logs via
   `FiddlerApplication.Log.LogFormat` all show up there, no debugger or
   DebugView needed.

**Validated on a real machine, historically, under the retired
`Fiddler`-identity approach:** all five real extension samples this
project inspected (`AustralianImages`, `ContentBlock`, `JSFormat`,
`SAZClipboard`, `Differ`) loaded clean with zero load errors, and
`SAZClipboard.dll` specifically discovered, loaded, ran `OnLoad()`, and
added a working item to the real `mnuTools` `MenuItem`. See the design
doc for why that approach was retired in favor of the assembly-mismatch
diagnostic above -- none of the five samples bind here unmodified anymore,
by design; getting any of them working again now needs the recompile/
re-target step described above. `Utilities.ReadSessionArchive`/
`WriteSessionArchive` are unaffected by any of this (real implementations,
`SazArchive.cs` in the CompatShim project, not placeholders) -- see that
project's own README for what's still not supported (encrypted `.saz`
files).

## Automated tests

`Clearinet.LegacyExtensionHost.Tests/` (added to this folder's own
`Clearinet.LegacyExtensionHost.sln`, never the main `CLeARINET.sln`) covers
this host end to end:

```
dotnet test Clearinet.LegacyExtensionHost.sln
```

Everything that doesn't need any real extension DLLs always runs (round-
tripping a synthetic `.saz` archive through the real `SazArchive`
implementation, the `HTTPHeaders` null-on-miss regression guard, folder-
scanning edge cases). Two more groups need real DLLs and are split by what
those DLLs are compiled against, following the design doc's "Don't get
sued" decision:

- **`RealExtensionLoadingTests`** -- runs against the five original,
  UNMODIFIED real samples (`AustralianImages`, `ContentBlock`, `JSFormat`,
  `SAZClipboard`, `Differ`), looked for the same way this host's own
  `Program.cs` does (`%USERPROFILE%\Documents\CLeARINET\LegacyExtensions\`
  by default), overridable via the `CLEARINET_LEGACY_EXTENSIONS_DIR`
  environment variable. Since none of the five are compiled against this
  shim's own `Clearinet.CompatShim` identity, this locks in the
  assembly-mismatch behavior itself: all five get reported as mismatches
  (not loaded), the diagnostic messages actually explain what to do, the
  Log tab reflects every mismatch, and nothing partially loads or throws
  along the way. Skips itself gracefully (an inert pass, not a failure)
  when the folder isn't set up -- these are real third-party binaries, not
  part of the repo.
- **`PatchedExtensionTests`** -- optional, off by default, and separately
  gated by its own `CLEARINET_LEGACY_PATCHED_EXTENSIONS_DIR` environment
  variable, pointed at a folder of extension `.dll`s a developer has
  already recompiled or re-targeted against `Clearinet.CompatShim` (see
  `AssemblyMismatch`'s own diagnostic message for how). This is where the
  pre-"Don't get sued" assertions live now -- discovery/loading actually
  succeeding, real `mnuTools`/`mnuRules` menu items getting added, and
  feeding each loaded `IAutoTamper` a synthetic session through every hook
  to check nothing throws -- since that's no longer true of the five
  samples unmodified. Nothing in this repo ships a patched copy of any
  real extension (that would mean redistributing a modified third-party
  binary -- see the CompatShim README's own redistribution note), so this
  group skips itself gracefully until a developer points it at their own
  local folder.

An optional test also reads a real captured `.saz` file if one is pointed
at via `CLEARINET_LEGACY_TEST_SAZ`.

Two more groups need no real extension DLLs at all and always run,
covering the session bridge itself:

- **`SessionBridgeRunnerTests`** -- direct, no-pipe tests of
  `SessionBridgeRunner`/`SessionMapping` against small fake `IAutoTamper`
  implementations: a wire message becomes a correct `Session`, an
  extension's edit to it comes back out correctly, one throwing extension
  doesn't stop the others, and a repeated header name is handled (logged,
  not thrown on).
- **`SessionBridgeServerTests`** -- the same round trip for real, over an
  actual `NamedPipeClientStream` talking to a real, running
  `SessionBridgeServer` (started once for the whole test run by
  `SessionBridgeServerFixture`, since the pipe name can only be bound so
  many times concurrently -- see that fixture's own remarks). Proves the
  wire framing and JSON serialization actually work together, not just
  the request-handling logic the tests above already cover directly.

Deliberately not attempted: driving `Differ`'s own internal "Load SAZ
Files"/"Compare" UI via reflection into its private controls. That would
be fragile (reaching into another DLL's own implementation details, not
its public contract) and wouldn't actually test anything this project
controls -- the `HTTPHeaders` regression test above is what actually
guards the confirmed root cause of that crash (see the design doc).
