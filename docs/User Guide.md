# CLeARINET User Guide

This is the end-user guide: how to actually use the app once it's running.
If you're looking for build instructions, project layout, or the "why" behind
a design decision, see [README.md](../README.md) and the other documents in
this `docs/` folder instead — those are written for contributors, not for
someone just trying to inspect some traffic. You can always get back to this
guide from inside the app itself via **Help > Documentation**.

## Starting the proxy

1. Pick a port. **Port: Auto** (the default) lets the OS hand back any free
   port — the simplest choice unless something else on your machine expects
   CLeARINET on a specific port. **Specify** lets you type one in.
2. Click **Start**.
3. The first time you do this, you'll be asked to trust a new certificate
   named `DO_NOT_TRUST_ClearinetRoot...`. This is expected — it's what
   lets CLeARINET see inside HTTPS traffic on this machine, the same
   mechanism Fiddler Classic uses. On Windows this is the OS's own "Do you
   want to install this certificate?" prompt; on macOS it's a confirmation
   dialog CLeARINET shows itself (macOS has no equivalent OS-level prompt
   for the way CLeARINET installs it) before it ever touches your login
   keychain. Accept it to continue. Nothing captured ever leaves your
   device on its own.
4. Once running, the status bar at the bottom shows which port you're
   listening on and whether CLeARINET successfully registered itself as
   the system proxy. If it couldn't (some other tool already has that
   role, for example), you'll still capture traffic from anything you point
   at the port manually.

Click **Stop** to release the port and stop capturing. Sessions already
captured stay in the list until you close the app.

CLeARINET's HTTPS interception (certificate trust and system proxy
registration) is implemented on both Windows and macOS. The macOS side is
new and hasn't been verified against a real Mac yet — see
[README.md](../README.md)'s Known limitations section for the specifics.

## The session list and inspectors

Every request/response pair CLeARINET sees appears as a row in the main
grid: number, time, status, method, URL, and request/response sizes. Click a
row to see its **Request** and **Response** panels below, each with its own
tabs:

- **Headers** — a table of header names and values.
- **Raw** — the decoded text body (automatic decompression for gzip,
  deflate, and zstd — see README.md for the full list of what's handled).
- **Hex** — a hex dump of the raw bytes.

### Filtering

The filter box above the grid accepts free text (matched against the URL and
headers, never body content) plus a small query grammar:

- `method:GET` — exact method match
- `host:example.com` — exact host match
- `status:404`, `status:4xx`, `status:>=500` — exact, class, or comparison
  forms

Combine free text and query tokens in the same filter; every keystroke
re-applies it live.

## The Tools menu

Several panels are hidden by default to keep the main screen uncluttered.
**Tools** in the menu bar is where you turn each one on or off — every
entry there is a checkbox, and checking it shows the panel on the main
screen (unchecking it hides it again without losing whatever you'd set up
in it):

| Tools menu entry | Shows |
|---|---|
| FiddlerScript | The FiddlerScript panel (see below) |
| Extensions | The Extensions status panel (see below) |
| Legacy Extension Host | The Legacy Extension Host panel (see below) |
| Break on Requests | Pauses every outgoing request at a breakpoint |
| Break on Responses | Pauses every incoming response at a breakpoint |
| Also Break On | A row of narrower breakpoint conditions (URL contains, method, status code) |
| AutoResponder | The AutoResponder rules editor |

Unchecking "Break on Requests"/"Break on Responses"/"Also Break On" only
hides that control — it doesn't clear whatever rule you'd already set. If a
connection is genuinely paused at a breakpoint, the Breakpoints panel stays
visible regardless of these checkboxes, since hiding it would hide your only
way to resume or abort that connection.

Each visible panel can also be collapsed on its own (the small arrow in its
header) without hiding it entirely from the Tools menu — useful if you want
to temporarily reclaim screen space without losing your rules.

## Breakpoints

Fiddler-Classic-style: pause a request or response before it completes, look
at (and optionally edit) its raw text, then either **Resume** it on its way
or **Abort** the connection outright.

1. Check **Tools > Break on Requests** and/or **Break on Responses** to pause
   everything, or check **Tools > Also Break On** for narrower conditions:
   pause a request whose URL contains some text, a request with a specific
   method, a response whose request URL contains some text, or a response
   with a specific status code.
2. When something hits a breakpoint, it appears in the **Breakpoints**
   panel's list on the left. Select it to see its raw request/response text
   on the right — start line, headers, and body together, as one editable
   block of text.
3. Edit the text if you need to, then click **Resume** to let it continue
   (with your edits applied) or **Abort** to kill the connection instead. An
   edit that leaves the text malformed is reported as an error rather than
   silently dropped, so a bad edit won't slip through unnoticed.

## AutoResponder

Fiddler Classic's traffic-replay feature: an ordered list of match/action
rules that intercept matching requests before they ever reach the real
server.

1. Check **Tools > AutoResponder** to show the rules editor.
2. Click **Add Rule** to add a new (initially disabled) row.
3. Fill in the **Match** column — a plain substring, a `*wildcard*`, or one
   of Fiddler's own prefixes: `EXACT:`, `regex:`, `NOT:`, `METHOD:verb`.
4. Fill in the **Action** column — a local file path or `http(s)://` URL to
   serve instead, or one of Fiddler's action prefixes: `*redir:`, `*delay:ms`,
   `*header:Name=Value`, `*reset`, `*drop`, `*bpu`, `*bpafter`, and others.
5. Check the box on the left of the row to enable it. Rules only take effect
   once both the row itself is checked *and* the panel's own toggle
   (**Tools > AutoResponder**, which doubles as Fiddler's own "Enable rules"
   switch) is on.
6. Use **Move Up**/**Move Down** to reorder rules — the first matching rule
   wins, so order matters when two rules could both match the same request.
   **Remove** deletes the selected rule.

## FiddlerScript

FiddlerScript lets a script hook into every request and response, and
optionally add its own menu entries, buttons, and grid columns. This is the
closest thing to Fiddler Classic's own `CustomRules.js`.

1. Check **Tools > FiddlerScript** to show the panel.
2. Click **Browse…** to pick a `.js` file (or any file — the picker defaults
   to `.js` but doesn't require it).
3. Click **Load**. The status line below shows which handlers the script
   defines (`OnBeforeRequest`, `OnBeforeResponse`) or, if something's wrong
   with the script, its load error — the previous working script (if any)
   keeps running until you fix and reload.
4. After editing the script file in whatever editor you normally use, click
   **Reload** to pick up your changes. A script can also trigger its own
   reload from inside itself.

A loaded script can add to the app in a few places beyond the two request/
response hooks:

- **Rules menu** (in the menu bar): populated from the script's declared
  rule options and choices. Empty and greyed out when nothing's loaded, or
  the loaded script declares none — hover over it for a reminder of why.
- **Script Actions** (a row of buttons inside the FiddlerScript panel
  itself, right below the Load/Reload buttons): one button per action the
  script declares. Only appears at all once the loaded script actually
  declares at least one.
- **Right-click on a session row**: a script can add its own entries to that
  context menu, operating on whichever session you right-clicked.
- **Extra grid columns**: a script can declare its own columns, which are
  added to the session grid after the built-in ones once the script loads.

## Extensions

Compiled .NET extensions (the successor to Fiddler Classic's `.dll`-based
extension model) are loaded automatically from CLeARINET's Extensions
folder at startup — there's no in-app step to load one, only to drop the
`.dll` there before launching CLeARINET.

Check **Tools > Extensions** to see a status readout: which folders were
scanned, how many `.dll` files were found, how many of each extension role
(AutoTamper, request/response inspector, importer, exporter, exec-action
handler) actually loaded, and any load errors.

If a loaded extension proffers session import or export, two File-menu
entries pick it up:

- **File > Import via Extension…**
- **File > Export via Extension…**

Both are disabled (greyed out, with a tooltip explaining why) until a
loaded extension actually supports that direction. There's no per-format
picker if more than one extension or format is available — the first loaded
extension and its first proffered format are always used.

## Legacy Fiddler Classic extensions

A separate, optional tool for a specific case the Extensions feature above
can't cover: an already-compiled Fiddler Classic extension `.dll` whose UI
hooks reach directly into Windows Forms menu/toolbar/tab controls that
Microsoft removed from .NET 5 and later. Those extensions can't run inside
CLeARINET's own process at all — not a bug, a platform limitation — so
`Clearinet.LegacyExtensionHost.exe`, a separate program included with
CLeARINET, runs them instead, in its own window, wired up to your real
captured traffic.

This is off by default and entirely optional; skip this section if you
don't have an existing compiled Fiddler Classic extension you specifically
want to keep using.

1. Drop the extension's `.dll` into `Documents\CLeARINET\LegacyExtensions\`.
   An extension still compiled against the real Fiddler assembly won't load
   as-is — see the pop-up/log message it produces for what to do, which
   comes down to recompiling it (if you have its source) or re-targeting
   its compiled metadata with the `Retarget-LegacyExtension.ps1` script
   included alongside `Clearinet.LegacyExtensionHost.exe`.
2. Check **Tools > Legacy Extension Host** to show the panel, then check
   "Launch Clearinet.LegacyExtensionHost.exe automatically on Start". With
   that on, clicking **Start** also launches the legacy host (if it isn't
   already running) and connects it to your real traffic; the panel's
   status line reports what happened (already running, launched, not
   found, or launch failed). Leave the checkbox off and nothing about your
   normal CLeARINET usage changes.
3. The legacy host opens as its own separate window, not merged into
   CLeARINET's own — that's a known, permanent limitation, not a bug.
   Loaded extensions' request/response-tampering hooks run against your
   real traffic either way; anything the extension shows in its own
   window's session list is separate demo data, not your real sessions.

## Saving and loading captured sessions

- **File > Save SAZ…** exports every session captured this run to a
  `.saz` (Session Archive Zip) file on your Desktop.
- **File > Open SAZ…** imports sessions from a previously saved `.saz` file
  back into the session list, exactly as if they'd just been captured live.

## The Help menu

- **Help > Documentation** opens this file through whatever your system
  uses to open `.md` files. If nothing's registered for that extension,
  you can also find this file directly at `docs/User Guide.md` in the
  CLeARINET source tree, or, for a running build, in a `Documentation`
  folder next to the app's executable.
- **Help > About CLeARINET** shows which build you're actually running —
  useful when reporting an issue, or checking whether you're on the
  latest preview.

## Known limitations

CLeARINET is an early, working MVP, not a 1.0 release. See
[README.md](../README.md)'s Known limitations section for what's
deliberately not here yet (settings persistence across runs, HAR/Netlog
import, replay beyond AutoResponder, macOS support unverified on a real
Mac, and more) — that list is kept in one place, in README.md, rather than
duplicated here.
