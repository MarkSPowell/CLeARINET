# CLeARINET Preferences Design

CLeARINET has had no app-level settings persistence so far. The port, the
Tools-menu panel toggles and the filter text all reset on every launch
(README, "Known limitations"). This doc designs the Preferences system that
fixes that. It also covers two commitments the Project Plan already makes
for whenever this gets built:

- **Upstream Issue #2, "Extensible Lists"**: any list of data that may
  change comes from an overridable preference, named
  `clearinet.config.<category>.<name>`, with semicolon-separated values.
- **Deadlock safety**: Eric Lawrence names Fiddler's `about:config`-style
  preferences system, built to avoid deadlocks in a multithreaded,
  extensible app, as one of the few parts he's proud of. See "Lessons from
  Fiddler's own history" in the Project Plan.

A hard requirement for this feature: **Windows and macOS behave the same.**
The same keys, the same file format and the same semantics apply on both.
A preferences file copied from one platform to the other loads without
conversion. Everything that could differ between the platforms is covered
in "Platform parity," below.

## Goals and non-goals

**Goals**

- Every app-level setting a person changes in the UI survives a restart,
  on both platforms.
- One reusable store in `Clearinet.ProxyCore` with no UI-toolkit types
  (tenet 4). Any host can use it, not just `Clearinet.DesktopUi`.
- An API shaped like Fiddler's own `IFiddlerPreferences` (tenet 1), so
  code written against Fiddler's preferences maps across one method at a
  time.
- Typed list values as a first-class accessor from day one (Issue #2).

**Non-goals for this milestone**

- A Preferences dialog or an `about:config` editor. The store is built so
  one can sit on top later, but for now settings change through the
  existing UI controls.
- Watching the file for edits made while the app is running. The file is
  read once at startup. See "Why no file watcher" below.
- Syncing preferences across machines.
- Persisting AutoResponder rules. Those belong in their own file (Fiddler
  Classic has its own AutoResponder rules format), which is a separate
  follow-up.

## What gets persisted

| Key | Type | Default | Bound to |
|---|---|---|---|
| `clearinet.proxy.port.auto` | bool | `true` | Port: Auto / Specific |
| `clearinet.proxy.port` | int | `8888` | Specific port value |
| `clearinet.ui.panels.fiddlerscript` | bool | `false` | Tools → FiddlerScript |
| `clearinet.ui.panels.extensions` | bool | `false` | Tools → Extensions |
| `clearinet.ui.panels.legacyhost` | bool | `false` | Tools → Legacy Extension Host |
| `clearinet.ui.panels.alsobreakon` | bool | `false` | Tools → Also Break On |
| `clearinet.ui.filter.text` | string | `""` | Session filter box |
| `clearinet.fiddlerscript.path` | string | `""` | FiddlerScript panel's path box |
| `clearinet.extensions.legacyhost.autolaunch` | bool | `false` | Legacy host auto-launch |

The legacy extension host only runs on Windows (it's a `net48` process).
Its auto-launch key still exists on macOS for file parity. It's read and
written the same way there and has no effect.

### What is deliberately *not* persisted: armed breakpoints

The README listed breakpoint conditions among the things that reset. On
closer look, that reset is a safety feature and should stay. CLeARINET
registers itself as the **system** proxy. A breakpoint that is silently
re-armed at startup (Break on All Requests, or a `bpu` on a common host)
would stall every app on the machine as soon as Start is clicked, with no
obvious reason why. So:

- `BreakOnAllRequests` / `BreakOnAllResponses` always start off.
- The `bpu` / `bpafter` / `bpm` / `bps` values always start empty.
- Only the *visibility* of the "Also break on" row persists
  (`clearinet.ui.panels.alsobreakon`).

If users ask for
persisted conditions, the safe version is to persist them *disarmed*, as
remembered values that need one click to arm again. That can be added
later without changing the file format.

The AutoResponder's on/off switch isn't persisted for the same reason. It
changes what every app on the machine receives, and the rules it would
switch on aren't persisted yet anyway.

## Naming

- App settings use `clearinet.<area>.<name>`, all lowercase, dot-separated.
- Lists use `clearinet.config.<category>.<name>`, exactly as Issue #2
  specifies. No list-valued key exists yet. The first will likely be
  per-process capture's browser list,
  `clearinet.config.processnames.browsers`.
- Names that contain `ephemeral` (any case) are kept in memory for the
  running process and never written to disk. This follows Fiddler's
  `fiddlerscript.ephemeral.*` convention, which `FiddlerScriptPreferenceStore`
  already implements, so there's one rule everywhere.
- **Names are case-insensitive** (`StringComparer.OrdinalIgnoreCase`).
  Windows file names and the registry are case-insensitive, and so is
  macOS's default APFS volume. A case-sensitive store would be the one
  place in the app where `Clearinet.UI...` and `clearinet.ui...` meant
  different things. Stored keys keep the casing they were first written
  with.
- A valid name is non-empty, at most 256 characters, contains no
  whitespace or control characters and has no leading or trailing dot.
  Invalid names throw `ArgumentException` on write, and reads return the
  default.

## API (`Clearinet.ProxyCore.Preferences`)

```csharp
public interface IPreferenceStore
{
    string? this[string prefName] { get; }

    string GetStringPref(string prefName, string defaultValue);
    bool   GetBoolPref(string prefName, bool defaultValue);
    int    GetInt32Pref(string prefName, int defaultValue);
    IReadOnlyList<string> GetListPref(string prefName, IReadOnlyList<string> defaultValue);

    void SetStringPref(string prefName, string value);
    void SetBoolPref(string prefName, bool value);
    void SetInt32Pref(string prefName, int value);
    void SetListPref(string prefName, IEnumerable<string> values);
    void SetPrefs(IEnumerable<KeyValuePair<string, string>> prefs);
    void RemovePref(string prefName);

    PrefWatcher AddWatcher(string prefixFilter, EventHandler<PrefChangeEventArgs> handler);
    void RemoveWatcher(PrefWatcher watcher);
}
```

Every member except `GetListPref` and `SetListPref` matches a member of
Fiddler's documented `IFiddlerPreferences` by name and shape. That makes a
later adapter for FiddlerCore-style extensions (`FiddlerApplication.Prefs`)
a thin wrapper rather than a redesign.

### Value encoding

Under the hood every value is a string, as in Fiddler. The typed
accessors are conversions at the edge:

- **bool**: written as `True` / `False` (the output of `bool.ToString()`, as
  in Fiddler). When reading, any case of `true`/`false` is accepted.
  Anything else returns the default.
- **int**: written and read with `CultureInfo.InvariantCulture`. This is a
  parity requirement, not just good hygiene. A Mac or PC set to a locale
  that groups digits differently must still read `8888` as 8888.
- **list**: joined with `;`. When reading, entries are trimmed, empty
  entries are dropped and duplicates are removed case-insensitively (the
  first occurrence is kept). Entries can't contain `;`. `SetListPref`
  throws if one does, rather than silently splitting it.

A value that fails to parse returns the default. It's never an exception.
A hand-edited file with a typo must not stop the app from starting.

## Storage

### Location

One directory for per-user app data, resolved once through
`ClearinetPaths.AppDataFolder`:

```csharp
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CLeARINET")
```

| Platform | Resolves to |
|---|---|
| Windows | `%LOCALAPPDATA%\CLeARINET\` (typically `C:\Users\<you>\AppData\Local\CLeARINET\`) |
| macOS | `~/Library/Application Support/CLeARINET/` |

This is the same expression `WinInetSystemProxy`, `MacOSSystemProxy` and
`FiddlerScriptPreferenceStore` already use. New code resolves it through
`ClearinetPaths`, so the next contributor can't pick a different folder by
accident. The three existing call sites can move over in a small
follow-up. They're left alone here because two of them are crash-recovery
code for the system proxy, which shouldn't change in the same pass. The macOS
mapping depends on .NET 8's change to `GetFolderPath` on Unix. Before
.NET 8, `LocalApplicationData` returned `~/.local/share` on macOS. CLeARINET
targets .NET 10, so `~/Library/Application Support` is guaranteed. A unit
test asserts this on each CI leg, so a future runtime change would fail CI
rather than quietly moving everyone's settings.

If `GetFolderPath` ever returns an empty string (it can, on Unix, when the
folder doesn't exist), `AppDataFolder` throws rather than resolving to a
relative path in whatever the working directory happens to be.

Neither location roams or syncs. Local AppData is excluded from Windows
roaming profiles. Application Support isn't synced to iCloud. The behavior
is the same on both: settings stay on the machine.

### File

`preferences.json` in that folder:

```json
{
  "formatVersion": 1,
  "preferences": {
    "clearinet.proxy.port": "8888",
    "clearinet.proxy.port.auto": "False",
    "clearinet.ui.panels.fiddlerscript": "True"
  }
}
```

- UTF-8 without a BOM, `\n` line endings and indented output, the same
  bytes on both platforms. Keys are sorted, so a diff between two
  machines' files only shows real differences.
- **Unknown keys are kept.** Keys this build doesn't know about (from a
  newer version, an extension or a hand edit) are carried through every
  load and save unchanged. An older build never deletes a newer build's
  settings.
- `formatVersion` exists so a future breaking change can migrate the file
  instead of guessing. This build reads version 1. A higher version loads
  **read-only**: values are used, but the file is never overwritten, since
  this build can't know what it would lose.

### Writing safely

The same sequence runs on both platforms:

1. Serialize the snapshot to `preferences.json.<random>.tmp` in the same
   folder.
2. Flush it to disk (`FileStream.Flush(flushToDisk: true)`: `FlushFileBuffers`
   on Windows, `fsync` on macOS).
3. `File.Move(tmp, preferences.json, overwrite: true)`. That's
   `MoveFileEx(MOVEFILE_REPLACE_EXISTING)` on Windows and `rename(2)` on
   macOS. Both replace the file atomically on the same volume, so another
   reader sees the old file or the new one, never half of each.
4. If the move fails with an `IOException` (on Windows, most often an
   antivirus or indexer briefly holding the target open), retry up to 5
   times with a short backoff. After that, give up for this write, delete
   the temp file and keep the in-memory value. The next change retries
   naturally.

A corrupt `preferences.json` (invalid JSON or the wrong shape) is renamed
to `preferences.json.corrupt-<UTC timestamp>` and the app starts with
defaults. The old store silently treated a corrupt file as empty and would
overwrite it on the next write. Renaming it keeps whatever the person had,
so they can fix the file by hand.

### When writes happen

`Set*` updates memory immediately and schedules a save 250 ms later.
Changes arriving in that window share one write. Typing in the filter box
doesn't write the file once per keystroke. `Flush()` saves synchronously
and is called from the view model's `Dispose`, which runs from
`ShutdownRequested` on both platforms (Windows window close, macOS
Cmd+Q / Quit). Disposing the store also flushes, and as a backstop the
store flushes itself from `AppDomain.ProcessExit`, which runs on any
normal exit on both platforms.

At worst, a crash within 250 ms of a change loses that one change. That's
the right trade for settings.

### More than one CLeARINET running

It's easy to launch two copies on Windows. On macOS, LaunchServices
normally prevents it, but running the binary directly or `open -n`
doesn't. The rule is the same on both: **last writer wins, per key.**

A save doesn't write this process's whole in-memory view. It:

1. Takes an exclusive lock on `preferences.lock`, opened with
   `FileShare.None`. On Windows that's a real sharing-mode lock. On macOS,
   .NET enforces `FileShare.None` between .NET processes with an advisory
   `flock`. Both processes are CLeARINET, so the effect is the same.
   Locking waits up to 2 seconds before giving up on that save.
2. Re-reads `preferences.json` from disk.
3. Applies only the keys *this process* changed since its last save
   (including removals).
4. Writes the result (see "Writing safely") and releases the lock.

The lock file itself is never deleted. Deleting it would open a window
where two processes each create and lock their own copy.

Process A changing the port and process B changing a panel toggle
therefore both survive. If both change the same key, the later save wins.

### Why no file watcher

`FileSystemWatcher` uses different mechanisms per platform
(`ReadDirectoryChangesW` on Windows, FSEvents on macOS), with different
coalescing and latency. An atomic-rename save like the one above shows up
as different event sequences on each. Rather than ship behavior that
diverges by platform, the file is read once at startup on both. A hand
edit takes effect on the next launch. A later `about:config` editor would
write through the store's own API, not the file.

## Threading and deadlock safety

This follows the Fiddler lesson above: the store must be safe to call
from any thread, including proxy connection threads and watcher
callbacks, without the risk of deadlock.

- **Reads never lock.** The current values are an immutable snapshot
  (`ImmutableDictionary<string,string>` using `OrdinalIgnoreCase`),
  swapped atomically. A read is one volatile field read plus a dictionary
  lookup.
- **Writes use one private lock**, held only to compute and swap the new
  snapshot and record the key as changed. No I/O, no callbacks and no
  other lock is taken while it's held.
- **Watcher callbacks run after the lock is released**, on the calling
  thread, in registration order. A watcher can freely call `Set*` again.
  That write takes the lock fresh and doesn't re-enter a held one. An
  exception thrown by one watcher is caught and logged, and the other
  watchers still run.
- **Disk I/O runs on the save timer's thread-pool thread** (or the caller
  of `Flush()`), with its own separate I/O lock. A slow disk never blocks
  a `Set*` caller, least of all a proxy connection thread.

There's never more than one lock held at a time, so no lock-ordering
cycle is possible.

## Platform parity checklist

Every row is either the same on both platforms by construction or
covered by a test that runs on both CI legs (`ci.yml` already runs
`windows-latest` and `macos-latest`).

| Concern | Windows | macOS | How parity is kept |
|---|---|---|---|
| Folder | `%LOCALAPPDATA%\CLeARINET` | `~/Library/Application Support/CLeARINET` | One resolver. A per-OS test asserts the expected parent |
| File bytes | UTF-8, `\n`, sorted | Same | Serializer settings fixed in code. Round-trip test on both legs |
| Key casing | Case-insensitive | Case-insensitive | `OrdinalIgnoreCase` everywhere. Test on both legs |
| Number/bool parsing | Invariant culture | Invariant culture | Test runs under a non-English culture on both legs |
| Atomic replace | `MoveFileEx` | `rename(2)` | `File.Move(overwrite: true)` |
| Transient lock on target | Retry ×5 | Retry ×5 (rarely needed) | Same code path |
| Multi-instance | `FileShare.None` lock file | Advisory `flock` via .NET | Same code path. Per-key merge test on both legs |
| Save on quit | `ShutdownRequested` → `Dispose` → `Flush` | Same (Cmd+Q) | Same code path |
| Crash loss window | ≤ 250 ms | ≤ 250 ms | Same timer |
| Corrupt file | Renamed aside, defaults | Same | Test on both legs |
| Newer `formatVersion` | Read-only | Read-only | Test on both legs |
| External edits while running | Not picked up | Not picked up | No watcher on either |
| Windows-only setting (legacy host) | Used | Stored but no effect | Same key set on both |

**Not yet verified on a real Mac**, like the rest of the macOS support.
CI's `macos-latest` leg exercises all of the above against a real APFS
volume and the real .NET runtime on macOS. The only macOS-specific
behavior CI can't reach is Cmd+Q triggering `ShutdownRequested` in the
packaged `.app`, which is Avalonia's own behavior and not something this
feature changes.

## FiddlerScript `[BindPref]`

`FiddlerScriptPreferenceStore` keeps its own file,
`fiddlerscript-prefs.json`, in the same folder, for now. Unifying the two
is a clear follow-up. Real Fiddler keeps script preferences and app
preferences in one namespace, and a single store would give BindPref
values the atomic-write and multi-instance behavior above. It needs a
one-time migration of the existing file, though, so it's kept out of this
first change to keep the change reviewable. The ephemeral-name rule is
already the same in both.

## Follow-ups

1. Move `FiddlerScriptPreferenceStore` onto `PreferenceStore`, migrating
   `fiddlerscript-prefs.json` once, on first launch.
2. An `about:config`-style editor (and QuickExec `prefs` / `about:config`
   commands), built on `SetPrefs`/`RemovePref` and a read of the full
   snapshot.
3. An `IFiddlerPreferences` adapter for compiled extensions, backed by
   this store.
4. First real list preference: `clearinet.config.processnames.browsers`,
   when per-process capture is built.
5. AutoResponder rules persistence, in their own file.
6. Window size and position. They're left out here because Avalonia's
   window placement differs across displays and platforms enough to
   deserve its own small design.
