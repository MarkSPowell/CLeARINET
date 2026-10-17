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
captured stay in the list until you remove them (see below) or close the
app.

CLeARINET's HTTPS interception (certificate trust and system proxy
registration) is implemented on both Windows and macOS. The macOS side is
new and has had little testing on a real Mac so far — see
[README.md](../README.md)'s Known limitations section for the specifics.

On macOS the app isn't notarized, so the first time you open it macOS says
it can't verify the developer. Open it anyway from **System Settings >
Privacy & Security > Open Anyway**; you only need to do this once.

## The session list and inspectors

Every request/response pair CLeARINET sees appears as a row in the main
grid: number, time, status, method, URL, and request/response sizes. Click a
row to see its **Request** and **Response** panels below, each with its own
tabs:

- **Headers** — a table of header names and values.
- **Raw** — the decoded text body (automatic decompression for gzip,
  deflate, Brotli and zstd, and the character set from Content-Type — see
  README.md for the full list of what's handled).
- **JSON** (only for a JSON body) — the body pretty-printed, so an API
  response is readable at a glance. Offered for JSON content types, and for
  a body with no useful content type that turns out to be JSON.
- **WebForms** (request side, only when there's something to show) — the
  query string and any form fields, one row per name and value, decoded.
  Form uploads (`multipart/form-data`) show each field, and each file's
  name, type and size.
- **ImageView** (only for an image) — the picture itself, with its format,
  pixel size and size in bytes. PNG, JPEG, GIF, WebP, BMP and ICO are
  recognised even when the Content-Type is wrong. For PNG, JPEG, GIF and
  WebP it also lists any **image bloat**: bytes that don't change how the
  image looks, such as EXIF camera data, XMP, editing-program data,
  comments, text, or data after the end of the image. Removing them makes
  the file smaller with no visible change. (EXIF can hold the photo's
  orientation, which browsers honour, so keep that tag if the image relies
  on it.)
- **Hex** — a hex dump of the raw bytes.
- **Cookies** (only on a side that has cookies) — on the request side,
  each cookie the browser sent; on the response side, each cookie the
  server set, with its attributes (Path, Domain, Expires, Secure, HttpOnly,
  SameSite...). A cookie browsers will refuse, such as `SameSite=None`
  without `Secure`, or a `__Host-`/`__Secure-` cookie that breaks its
  prefix's rules, gets a warning.
- **Notes** (request side, only when there are any) — notes an importer
  or extension attached to the session. For example, a NetLog capture
  recorded without bodies notes the response size the browser reported,
  which explains why the Resp column shows 0 B.

Right-click a session for **Compare Sessions**, **Replay**, **Edit in
Composer** and **Remove**, plus any actions a FiddlerScript adds.
Right-clicking a session selects it, unless it's already selected.

To remove sessions, use **Edit > Remove Selected Session** (or press
**Delete** in the session list) or **Edit > Remove All Sessions**. Removed
sessions are gone for good: they're no longer in the list or in what
**Save SAZ** writes.

The **Image Bloat** column shows, for image responses, the bloat bytes out
of the file's size and the percentage. Rows where bloat is at least a
quarter of the file (and at least 1 KB) are tinted orange, unless something
else has already coloured the row.

Importers, extensions and scripts can also mark sessions for the list: a
row can have its own background or text colour, be bold, italic or struck
through, or be hidden (it's still captured and saved). Extensions can add
columns to the list, too.

To choose which columns the list shows, **right-click any column header**
(or use **Tools > Session List Columns**) and check or uncheck columns.
Every column can be hidden except **#** and **URL**, including the
Waterfall and Image Bloat columns and columns a script or extension added;
**Show All Columns** brings them all back. Drag a header to move its
column, or drag the edge between headers to resize it.

CLeARINET remembers which columns are shown, their order and their widths
for next time. A script's or extension's column keeps its place and width
while that script or extension isn't loaded, and comes back there. (The URL
column always fills the space that's left.) **Reset Columns** in the same
menu shows every column again, in the original order and widths.

### Filtering

The filter box above the grid accepts free text (matched against the URL and
headers, never body content) plus a small query grammar:

- `method:GET` — exact method match
- `host:example.com` — exact host match
- `status:404`, `status:4xx`, `status:>=500` — exact, class, or comparison
  forms

Combine free text and query tokens in the same filter; every keystroke
re-applies it live.

### Timing, the waterfall and statistics

CLeARINET times each phase of every exchange, and shows it the way
browsers' developer tools do:

- The **Waterfall** column in the session list draws each session on a
  shared time axis, from the first session shown to the last one finishing.
  Each bar is split into phases: **Queueing** (grey: reading the request,
  scripts and extensions, breakpoints), **DNS lookup** (teal), **Initial
  connection** (orange), **TLS handshake** (purple), **Request sent**
  (blue-grey), **Waiting for server** (green: time to first byte) and
  **Content download** (blue). A request that reused an open connection
  has no DNS, connection or TLS phases. Hover over a bar for the numbers.
- The line under the list sums up what's shown: how many requests, how much
  was transferred, and when the last one finished.
- The **Timing** tab shows the selected session's phases with their
  durations, whether it used a new or reused connection (and through which
  upstream proxy), and how long it sat at a breakpoint, if it did. For
  HTTPS it also shows the TLS version and cipher suite CLeARINET agreed
  with the server, and the server's certificate.
- The **Statistics** tab sums up the sessions the filter shows: bytes sent
  and received (and what compression saved), breakdowns by type, status and
  host, the slowest requests, and easy wins such as text sent uncompressed,
  image bloat, and responses with no caching headers.

The times are measured at CLeARINET, so they don't include time the
browser spent before sending a request, and anything the browser served
from its own cache never reaches CLeARINET at all. Sessions imported from a
file without timings have no bar.

If you don't need timing, right-click a column header and uncheck
**Waterfall** to hide that column.

**Servers that ask for a client certificate (mutual TLS).** CLeARINET makes
its own connection to the server, so when a server asks for a client
certificate, it asks CLeARINET, and the browser never sees the request (or
shows its certificate prompt). CLeARINET records it instead: the Timing tab
says the server asked, and which certificate authorities it accepts, and the
session gets a **Notes** tab with the `x-client-certificate-requested` note.
CLeARINET doesn't send a client certificate yet, so a server that requires
one refuses the connection; the 502 page then says that's the likely reason.

### Finding sessions

**Edit > Find Sessions** (**Ctrl+F**) looks for text anywhere in the
captured sessions, including inside request and response bodies
(decompressed, and read with their character set, as the Raw tab shows
them). Unlike the filter, it doesn't hide anything: every matching session
is highlighted in yellow and the first one is selected. **F3** (or
**Next**) moves to the next match.

In the Find bar you can choose to look in requests and responses or just
one side, in headers and bodies or just one of them, match case, and use a
regular expression. "Headers" includes the request line with the full URL,
and the status line. Press **Enter** to find, and **Escape** (or **Close**)
to close the bar and clear the highlighting. Sessions captured after a
Find aren't highlighted until you find again.

### Comparing two sessions

To see what changed between two exchanges, such as a request that works
and one that doesn't, select both: click one, then **Ctrl+click** the
other (**Cmd+click** on a Mac). Then choose **Edit > Compare Sessions**
(**Ctrl+W**), or right-click either of them and choose **Compare
Sessions**.

A window opens with the older session on the left and the newer on the
right. The **Request** and **Response** tabs each show the request or
status line (with the full URL), the headers and the body, side by side.
Lines that differ are yellow, with the part that differs highlighted; a
line only on the left is red and a line only on the right is green. Each
tab's title says how many places differ. **Next difference** (**F8**) and
**Previous difference** (**Shift+F8**) jump between them.

The options above the tabs change what's compared:

- **Decompress bodies** (on): compares the content of compressed bodies,
  not the compressed bytes.
- **Format JSON and forms** (on): puts each JSON value, and each form
  field, on its own line, so a single changed value shows on its own line
  rather than as one long changed line.
- **Ignore header order** (off): sorts the headers by name first, so the
  same headers sent in another order don't count as different.

Binary bodies, such as images, are compared as a hex dump, or by size and
SHA-256 hash when they're over 64 KB. Two very different large bodies are
shown as one changed block rather than lined up line by line. You can
keep several Compare windows open while capturing.

### WebSocket messages

Apps that keep a connection open for live updates, such as chat, dashboards
and development servers, often use WebSockets. A WebSocket connection shows
in the session list as the request that opened it, with status **101**.
Select it and open the **WebSocket** tab to see its messages, as browsers'
developer tools do:

- **↑** is a message the client sent, and **↓** one it received. Each row
  shows when it was sent (seconds after the connection opened), its type
  (text, binary, close, ping or pong), its size and the start of its data.
- Select a message to see it in full below the list: JSON is indented,
  and binary data is shown as hex unless it's really text.
- The list keeps growing while the connection is open, and the heading
  says whether it's still open and how many messages have gone each way.
- **Hide pings and pongs** hides the keep-alive messages some connections
  send constantly.

Compressed messages (permessage-deflate) are decompressed, and messages
sent in several pieces are put back together. The Size column shows a
compressed message's size as sent, then as decompressed. CLeARINET keeps up
to 1 MB of each message and 64 MB per connection; beyond that it counts
messages but doesn't keep their content. **Find** searches text messages
too: sent messages count as the request, received ones as the response.

WebSocket messages are saved in HAR files (and read from the HAR files
browsers export), but not in SAZ files yet.

## TextWizard

**Tools > TextWizard…** (**Ctrl+E**) encodes and decodes text. Type or
paste into the top box, choose a conversion, and the output updates as you
type. If text is selected in an inspector (or any text box) when you press
Ctrl+E, it becomes the input.

| Conversion | Does |
|---|---|
| To Base64 / From Base64 | Base64. Decoding also accepts the URL-safe kind (`-` and `_`), missing `=` padding and line breaks |
| URL encode / URL decode | Percent-encoding. Decoding treats `+` as a space |
| HTML encode / HTML decode | HTML entities such as `&lt;` and `&#39;` |
| To hex / From hex | Bytes as hex. Decoding ignores spaces, commas, colons, dashes, `0x`, `\x` and `%` |
| JavaScript escape / unescape | String escapes: `\n`, `\"`, `\uXXXX`, `\u{...}`, `\xHH` |
| Decode JWT | A JSON Web Token's header and payload, with `iat`, `nbf` and `exp` as dates. The signature isn't checked |
| Format JSON | JSON indented, one value per line |
| Unix time ↔ date | A Unix time (seconds or milliseconds) as a UTC and local date, or a date as a Unix time |
| Hashes | MD5, SHA-1, SHA-256 and SHA-512 |
| Deflate to Base64 / Inflate from Base64 (SAML) | The compression SAML's redirect binding uses. Inflating also accepts a URL-encoded value copied from `SAMLRequest=`, and zlib or gzip data |

**Characters as** chooses how text becomes bytes, and bytes text: UTF-8
(the default), Latin-1 or UTF-16. It's greyed out for the conversions where
it doesn't matter. When decoded bytes aren't text in that encoding (an
image, or compressed data, say), the output is a hex dump and a note says
so. **Use output as input** moves the result up to convert it again, for
example URL decode, then From Base64. The window remembers the last
conversion you used.

## The Tools menu

Several panels are hidden by default to keep the main screen uncluttered.
**Tools** in the menu bar is where you turn each one on or off — those
entries are checkboxes, and checking one shows the panel on the main
screen (unchecking it hides it again without losing whatever you'd set up
in it). Tools also opens TextWizard and the column chooser:

| Tools menu entry | Shows |
|---|---|
| FiddlerScript | The FiddlerScript panel (see below) |
| Extensions | The Extensions status panel (see below) |
| Legacy Extension Host | The Legacy Extension Host panel (see below) |
| Break on Requests | Pauses every outgoing request at a breakpoint |
| Break on Responses | Pauses every incoming response at a breakpoint |
| Also Break On | A row of narrower breakpoint conditions (URL contains, method, status code) |
| AutoResponder | The AutoResponder rules editor |
| Connections | The upstream proxy setting and "Allow remote computers to connect" (see below) |
| TextWizard… | Encodes and decodes text (see "TextWizard") |
| Session List Columns | Which columns the session list shows (see "The session list") |

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

Rules are saved as you edit them, with each row's checkbox, and are back
the next time you open CLeARINET. The panel's own switch always starts
off (see "Settings that are remembered").

## Composer and Replay

The **Composer** tab (next to **Inspectors**) sends a request you've
written or edited, like Fiddler Classic's Composer. Type the request as raw
HTTP: a first line with the method, a full `https://` or `http://` URL and the version,
then headers, a blank line, and any body. Click **Execute**.

```
POST https://api.example.com/items HTTP/1.1
Content-Type: application/json

{"name": "test"}
```

To start from something you've captured, select it and use **Edit > Edit in
Composer**. **Edit > Replay Selected Session** (or **R** in the session
list) sends the selected request again, unchanged, like Fiddler Classic's
Reissue.

Both send through CLeARINET itself, so capture has to be running. What they
send shows up as a new session, and goes through the AutoResponder,
breakpoints, FiddlerScript and extensions just like traffic from a browser.
The Host header is set from the URL, and Content-Length from the body. The
URL can be `http://` or `https://` (a path with no URL is sent as HTTPS).
Replay sends the captured request byte for byte, whatever its body.

**Bodies that aren't text.** Under the request, **Body** says where the
body comes from:

- **In the request text** (the default): typed after the blank line, and
  sent as UTF-8 text.
- **Hex (exact bytes)**: typed as hex in the box that appears, such as
  `89 50 4E 47 0D 0A`. Spaces and line breaks don't matter. Use it for
  images, protobuf, compressed or other binary bodies.
- **From a file**: a file you choose with **Browse…**, read when you click
  Execute. Set the Content-Type header yourself.

With Hex or a file, leave nothing after the blank line in the request text.
**Edit in Composer** on a session whose body isn't text puts the body in
the hex box for you, so it goes out exactly as captured.

## QuickExec

The box under the session list is **QuickExec**, Fiddler Classic's command
line. Click it or press **Alt+Q** (**Edit > QuickExec**), type a command and
press **Enter**. **Up** and **Down** bring back earlier commands, and
**Escape** clears the box. What a command did shows on the status line.

| Command | Does |
|---|---|
| `?text` | Selects sessions whose URL contains the text |
| `@host` | Selects sessions whose host contains the text |
| `=404` | Selects sessions with that status code |
| `=POST` | Selects sessions with that method |
| `>40k`, `<1k` | Selects sessions whose response body is larger or smaller (bytes, `k` or `mb`) |
| `select type` | Selects sessions whose response Content-Type contains the text (`select image`) |
| `allbut type` (or `keeponly`) | Removes every session whose Content-Type doesn't contain the text |
| `cls` (or `clear`) | Removes all sessions |
| `tail 100` | Keeps only the newest 100 sessions |
| `bpu text` | Pauses requests whose URL contains the text (`bpu` on its own stops) |
| `bpafter text` | Pauses responses to requests whose URL contains the text |
| `bps 404` | Pauses responses with that status code |
| `bpm POST` (or `bpv`) | Pauses requests with that method |
| `go` | Lets every paused session continue |
| `start`, `stop` | Starts or stops capturing |
| `help` | Lists the commands and opens this guide |

Selection commands select among the sessions the filter shows, so you can
then compare, replay or remove them. The breakpoint commands set the same
conditions as **Tools > Also Break On**, and show that row.

Any other command goes to the loaded FiddlerScript's `OnExecAction`
function (with the command split into words; "quoted text" stays one word),
then to extensions that handle commands (`IHandleExecAction`). The first to
return true has handled it.

## Connections: upstream proxy and other devices

**Tools > Connections** shows two settings.

**Upstream proxy.** On a network where the internet is only reachable
through a proxy (common at work), CLeARINET has to send its own connections
through that proxy too. Leave the box empty and CLeARINET uses whatever
proxy the computer was set to before you clicked Start; the status line
says what it chose. Type `host:port` to use a specific proxy, or `none` to
connect directly. Changes apply straight away.

If the computer uses an automatic proxy configuration script ("Use setup
script" on Windows, "Automatic proxy configuration" on macOS) or
"Automatically detect settings", CLeARINET fetches the script when you
click Start and asks it which proxy to use for each site, as a browser
would; the status line shows the script's address once it has loaded. If
the script can't be found or loaded, CLeARINET uses the computer's manual
proxy setting if there is one, or connects directly, and the status line
says why. While capturing, CLeARINET turns those automatic settings off
(browsers would otherwise skip CLeARINET) and turns them back on when you
click Stop.

**Proxy sign-in.** If the upstream proxy asks for a sign-in, CLeARINET
signs in as you, the way browsers do on a company network (Windows
sign-in: Negotiate, which is Kerberos, or NTLM). To use a different
account, or for a proxy that wants a plain user name and password
(Basic), enter them on the **Proxy sign-in** line. Type `DOMAIN\user` for
a domain account. The password is kept only while CLeARINET is running
and is never saved. If signing in doesn't work, the 502 page CLeARINET
shows says what the proxy offered.

CLeARINET can't yet use a SOCKS proxy.

**Allow remote computers to connect.** Lets phones, tablets and other
computers use CLeARINET as their proxy, so their traffic shows up
here. Check it, then click Start (it takes effect on the next Start; your
firewall may ask whether to allow it). The row then shows the address and
port to set as the proxy on the other device. On that device, with the
proxy set, open `http://clearinet/` to download CLeARINET's root
certificate, and follow the instructions on that page to install and trust
it. Anyone on your network can use the proxy while this is on, so turn it
off when you've finished, and remove the certificate from the other device.

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
`.dll` there before launching CLeARINET. That folder is
`Documents\CLeARINET\Extensions` on Windows and
`~/Documents/CLeARINET/Extensions` on macOS; CLeARINET also loads the
`Extensions` folder next to the app itself, where the Windows installer
puts the optional extensions below. If the same `.dll` is in both, the copy
in your Documents folder is used.

### Optional extensions included with CLeARINET

Three extensions come with the installers. Each is a separate work by its
author, under its own licence (see `THIRD-PARTY-NOTICES.txt` and the
`licenses` folder that come with them):

| Extension | What it does | Windows installer |
|---|---|---|
| NetLog importer (`CLeARINETNetLog.dll`) | Imports Chromium/Edge NetLog captures | Ticked by default |
| CSP Rule Collector (`CLeARINETCSP.dll`) | Builds a Content-Security-Policy for the sites you browse | Ticked by default |
| Privacy Scanner (`PrivacyScanner.dll`) | Colours responses that set cookies and checks P3P headers | Unticked (P3P is obsolete; mostly an example extension) |

- **Windows:** choose them on the installer's "Select Additional Tasks"
  page. Run the installer again to add or remove one.
- **macOS:** the `.dmg` has an **Optional Extensions** folder next to the
  app. Copy the `.dll`s you want into `~/Documents/CLeARINET/Extensions`
  and restart CLeARINET.

Check **Tools > Extensions** to see a status readout: which folders were
scanned, how many `.dll` files were found, how many of each extension role
(AutoTamper, request/response inspector, importer, exporter, exec-action
handler, ported Fiddler extension) actually loaded, and any load errors.

If a loaded extension proffers session import or export, two File-menu
entries pick it up:

- **File > Import via Extension…**
- **File > Export via Extension…**

Both are disabled (greyed out, with a tooltip explaining why) until a
loaded extension actually supports that direction. If your extensions
offer more than one format, you're asked which to use, with the one you
picked last time already selected. With only one, it's used straight away.

### Fiddler Classic extensions ported from source

A Fiddler Classic extension whose source you have can usually be rebuilt
for CLeARINET with very small changes, and then runs on both Windows and
macOS. The first one proven this way is Eric Lawrence's
[NetLog importer](https://github.com/ericlaw1979/FiddlerImportNetlog),
which imports Chromium/Edge NetLog captures (`.json`, `.json.gz` or a
`.zip` containing one). The only change to its source is deleting its
`using Fiddler;` lines. It's one of the optional extensions above (or see
`tests/ExtensionPorts/README.md` to build it yourself). Use
**File > Import via Extension…**; you'll be asked to pick the capture file.

Ported extensions can also work on live traffic (Fiddler's
`IAutoTamper` hooks): each request gets one session object from its first
hook to its last, flags they set show on the session's **Notes** tab, and
an extension can answer a request itself without it reaching the server.
An extension can add its own tab next to **Inspectors** above the request
and response panels; the tab strip only appears once one has.

The **CSP Rule Collector**, which works out the smallest
Content-Security-Policy a page needs, has a CLeARINET-only version:
[CSP-CLeARINET-Extension](https://github.com/MarkSPowell/CSP-CLeARINET-Extension),
a fork of the Fiddler extension with an Avalonia tab. It's one of the
optional extensions above (or build it yourself; see its README). Open
the **CSP Rule Collector** tab,
tick **Enable Rule Collection**, clear the browser's cache and browse the
site. Untick it when you're done: while it's on, responses get extra
report-only CSP headers and aren't cached. While it's collecting, the
browser sends many report requests to `fiddlercsp.deletethis.net`; the
extension answers them itself, and they're shown struck through. Tick
**Tools > Hide CSP Report Requests** to leave new ones out of the list,
and use **Edit > Remove All Sessions** to clear ones already there.

Ported extensions can add their own menus (before **Help**, or in
**Tools**), session-list columns and row colours; the optional Privacy
Scanner adds a **Privacy** menu, a **Privacy Info** column and row colours. See
`docs/Extension Test Targets.md` for what's supported so far.

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
   comes down to recompiling it against `Clearinet.CompatShim` (if you
   have its source).
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
- **File > Export HAR…** saves every session as an HTTP Archive (`.har`),
  the format browsers' developer tools and many support teams use. Bodies
  are saved decompressed, as text where they're text, and each session's
  timings are included (see "Timing, the waterfall and statistics").
- **File > Import HAR…** adds sessions from a `.har` file, such as one
  exported from a browser's developer tools. HAR files keep bodies
  decompressed, so imported sessions don't have Content-Encoding headers.
  The browser's own timings come in too, so the waterfall shows them.

## Settings that are remembered

CLeARINET remembers these settings between runs, the same way on Windows
and macOS:

- The port choice (Auto or a specific port number)
- Which Tools-menu panels are shown (FiddlerScript, Extensions, Legacy
  Extension Host, Also Break On, Connections)
- The upstream proxy setting, the proxy sign-in user name (not the
  password) and whether remote computers may connect
- The window's size and position, and whether it was maximized
- The session list's columns: which are shown, their order and widths
- TextWizard's last conversion and character encoding
- The AutoResponder rules (in their own file, `autoresponder-rules.json`,
  next to `preferences.json`)
- The session filter text
- The FiddlerScript path you last entered
- Whether the legacy extension host launches automatically

Breakpoints and the AutoResponder's on/off switch are **not** remembered,
on purpose. Once CLeARINET is the system proxy, a breakpoint or
AutoResponder rule that switched itself back on at launch would affect
every app on your machine as soon as you clicked Start.

Settings are saved in a `preferences.json` file:

- **Windows:** `%LOCALAPPDATA%\CLeARINET\preferences.json`
- **macOS:** `~/Library/Application Support/CLeARINET/preferences.json`

The file is the same format on both platforms, so you can copy it from one
machine to another. You can edit it by hand while CLeARINET is closed.
Changes made while it's running are picked up the next time it starts. If
the file ever becomes unreadable, CLeARINET renames it to
`preferences.json.corrupt-<date>` and starts with defaults, so nothing you
had is lost. To reset every setting, quit CLeARINET and delete the file.

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
deliberately not here yet (HTTP/2, proxies
that need a sign-in, little testing on a real Mac so far, and more) — that list is kept in one place, in README.md, rather than
duplicated here.
