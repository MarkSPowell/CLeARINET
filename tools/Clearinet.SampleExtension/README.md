# Clearinet.SampleExtension

An original, from-scratch sample extension for validating CLeARINET's
compiled `.NET` extension support (`ExtensionHost`, `LoadedExtensionSet`,
`Inspector2Adapter`, the `ISessionImporter`/`ISessionExporter` wiring) end
to end, against a real, separately-compiled `.dll` — the thing nothing in
this sandbox's own test suite could prove on its own (no local `dotnet`
build toolchain here; see `LoadedExtensionSetTests.cs`'s own remarks).

Not a port of `SAZClipboard.dll` or any other real Fiddler Classic
extension — every class here is written fresh for this validation pass,
the same clean-room posture as `samples/PhaseA2ValidationRules.js`.

## What's in here

| File | Interface(s) | What it does |
|---|---|---|
| `SampleAutoTamper.cs` | `IAutoTamper3` (→ `IAutoTamper2` → `IAutoTamper` → `IFiddlerExtension`) | Sets a marker header on every request/response it sees; counts every hook call, including the three nothing calls yet |
| `SampleExecActionHandler.cs` | `IHandleExecAction` | Recognizes one command, `sample.ping` |
| `SampleRequestInspector.cs` / `SampleResponseInspector.cs` | `IRequestInspector2` / `IResponseInspector2` | Prepends a one-line summary to whatever body they're given |
| `SampleSessionImporter.cs` | `ISessionImporter` | Returns two fixed, hardcoded sessions |
| `SampleSessionExporter.cs` | `ISessionExporter` | Writes a plain-text summary of whatever sessions it's given |

## Building and installing it

```
dotnet build tools\Clearinet.SampleExtension\Clearinet.SampleExtension.csproj -c Release
```

Then copy the built `Clearinet.SampleExtension.dll` (from
`tools\Clearinet.SampleExtension\bin\Release\net10.0\`) into
CLeARINET's own extensions folder — CLeARINET's own equivalent of
Fiddler's `My Documents\Fiddler2\Scripts`
(`ExtensionHost.DefaultExtensionsFolder`). Create that folder if it
doesn't exist yet.

**Careful: this folder is not always the literal `%USERPROFILE%\Documents`
path.** `ExtensionHost.DefaultExtensionsFolder` resolves through
`Environment.SpecialFolder.MyDocuments` — the Windows shell's own
registered Documents folder — and if OneDrive's "Known Folder Move"
feature has redirected that (as it commonly does; a repo cloned to
`...\OneDrive\Documents\GitHub\...`, the way this one likely is on your
machine, is a strong sign it has), the real folder is under
`%USERPROFILE%\OneDrive\Documents\CLeARINET\Extensions\` instead. The
desktop app's own "Extensions" status panel now says exactly which
folder it scanned and how many `.dll` files it found there — check that
line first, before assuming anything else went wrong.

Only the `.dll` itself needs to be there — `ExtensionHost` scans that one
folder non-recursively for `*.dll` files, so nothing else from the build
output needs to come along.

## What to check for, once it's dropped in and the desktop app starts

1. **Discovery/loading.** The desktop app's own "Extensions" status panel
   (in the main window, alongside the FiddlerScript status panel — no
   console needed at all) should show a line like `Scanned 'C:\...\Extensions':
   found 1 .dll file(s).` followed by a `Loaded: 1 AutoTamper, 1 request
   inspector, 1 response inspector, 1 importer, 1 exporter, 1 exec-action
   handler (total 6).` summary line — the first proves `ExtensionHost`
   looked in the right folder and actually saw the `.dll`; the second
   proves its `RequiredVersionAttribute` passed gating and its types got
   constructed and registered. If the scan line shows `found 0 .dll
   file(s)`, the `.dll` isn't where the app is actually looking — see the
   OneDrive/Documents note above. If instead the panel shows an `Error:`
   line (a bad `RequiredVersion`, a construction failure), something's
   wrong before any of the below could possibly work — start there.
2. **`IAutoTamper` on real traffic.** Start the proxy, browse to any plain
   HTTP page through it. Every request and every response should carry an
   `X-Clearinet-SampleExtension` header (`AutoTamperRequestBefore-ran` /
   `AutoTamperResponseBefore-ran`) — check the Headers inspector for any
   captured session, or a site that echoes your request back (e.g.
   `https://httpbin.org/post`) to see the request-side header survive the
   round trip. The console should also show an
   `AutoTamperRequestAfter`/`AutoTamperResponseAfter` line per session —
   note these two never touch the actual header, on purpose (see
   `IExtensionAutoTamperHost`'s own remarks on why the `*After` pair is
   fire-and-observe).
3. **`Inspector2`.** Select any captured session with a body. The Request
   and Response detail panes should each show a `SampleRequestInspector`/
   `SampleResponseInspector` tab, sorting after the built-in Headers/Raw/
   Hex tabs, with a `[SampleRequestInspector: N header(s), M original body
   byte(s)]` line prepended to the body text.
4. **`ISessionImporter`/`ISessionExporter`.** File → "Import via
   Extension…" should add two new rows to the session grid
   (`sample.clearinet.invalid`, one GET returning 200 and one POST
   returning 201). File → "Export via Extension…" (enabled once at least
   one session exists) should write `SampleExtension-Export.txt` next to
   wherever the desktop app's own executable/build output lives, one line
   per captured session.

## Running the companion unit tests

`tests/Clearinet.SampleExtension.Tests/` exercises every method on every
class above directly — no desktop app, no live traffic, no dropped `.dll`
needed:

```
dotnet test tests\Clearinet.SampleExtension.Tests\Clearinet.SampleExtension.Tests.csproj
```

This is the faster, first check: if these pass, every interface method is
implemented and does what its own doc comment says. The four
manual checks above are what additionally prove `ExtensionHost`'s
discovery/gating/`AssemblyLoadContext` loading and the desktop app's own
wiring work against a real, separately-compiled assembly — the one thing
a unit test running in-process, with this project already referenced
directly, can't prove on its own.
