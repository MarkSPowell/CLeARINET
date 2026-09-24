# Clearinet.LegacyExtensionHost.CompatShim

Builds `Clearinet.CompatShim.dll` -- an unsigned, net48 assembly
reproducing (from metadata only) the member surface the real, unmodified
compiled Fiddler Classic extension `.dll`s this project has inspected
actually call. Its own assembly identity is deliberately **not**
`Fiddler` -- see "Don't get sued" below.

**Status: naming question closed.** This assembly has been renamed
twice. It first shipped literally named `Fiddler`, specifically so real
extensions' compiled `AssemblyRef` metadata would resolve to it
unmodified -- retired in favor of `Clearinet.Fiddler`, which dropped the
binary-identity-substitution fact pattern but kept `Fiddler` as part of
its own name. It's now `Clearinet.CompatShim`, dropping `Fiddler` from
its own identity entirely -- see
`docs/CLeARINET .NET Extension Compatibility Design.md`'s "Update --
naming question closed: the second rename" for the full history (the
trademark reasoning, the licensing-history research, the "Don't get sued"
decision, and why the project has decided to treat this question as
resolved rather than wait on outside sign-off before shipping). Real
extensions compiled against the actual `Fiddler` assembly will NOT bind
to this unmodified -- see `LegacyExtensionLoader`'s `AssemblyMismatch`
diagnostic (in the sibling `Clearinet.LegacyExtensionHost` project) for
what happens instead, and what a user can do about it.

## Don't get sued

Project tenet 5 (see `docs/CLeARINET Project Plan and Goals.md`).
Presenting this assembly's own identity as the real `Fiddler` assembly --
so that real third-party binaries compiled against Progress/Telerik's own
Fiddler Classic bind to it as if it were the genuine article -- was judged
too much trademark exposure to ship, independent of how faithfully its
member surface was reproduced from clean-room metadata research. Naming
this assembly `Clearinet.CompatShim` instead removes that fact pattern
entirely: this project never claims, at the binary level, to be Fiddler,
and its own name no longer contains the word at all. What that costs: the
five real extensions this project validated end to end no longer load out
of the box -- each one needs to be recompiled (if its source is
available) or have its own `AssemblyRef` metadata re-targeted (a
metadata-only edit -- see `AssemblyMismatch`'s own remarks) before it
will bind here. Judged worth that cost. See the design doc for the full
reasoning, including why the August 2026 change to Fiddler Classic's own
licensing terms raised the stakes on this question, and why the project
ultimately decided that rename closed it rather than waiting further.

## Where every member came from

Every type and member in this project is reproduced from this project's
own read-only ECMA-335 metadata inspection of five real compiled Fiddler
Classic extensions (`AustralianImages.dll`, `ContentBlock.dll`,
`JSFormat.dll`, `SAZClipboard.dll`, `Differ.dll`) -- method/field names,
parameter types, and return types, decoded straight from each DLL's
`AssemblyRef`/`TypeRef`/`MemberRef` tables and signature blobs (ECMA-335
Partition II). No IL method body, decompiled logic, or `.cs` source file
was read to write any of it -- see the design doc's clean-room section.
Each file's own doc comments cite exactly which sample(s) confirmed which
member.

**What that does and doesn't guarantee:** the method/field *signatures*
here are real, confirmed data -- the CLR will actually bind these five
extensions' call sites against this assembly. The *behavior* behind many
of those signatures (what `CONFIG.GetPath` is really supposed to return,
what `frmViewer.actDoCompareSessions` is really supposed to show, what
`SessionStates` enum values Fiddler really used) is this project's own
reasonable placeholder, not confirmed -- flagged individually in each
file's remarks. Getting these five samples to *load without throwing* is
a different, smaller claim than getting them to *behave identically to
real Fiddler Classic*; only the former is claimed as done here.

## Known gaps

**The session bridge is built** (see
`Clearinet.LegacyExtensionHost/`'s own README, "The session bridge," and
the design doc's "The legacy host's session bridge -- built" section) --
a loaded extension's `IAutoTamper` hooks run against real `Session`
instances built from CLeARINET's own real proxied traffic, not synthetic
data. What that bridge does and doesn't cover, specifically:

- **Only `IAutoTamper`'s four hooks are bridged.** `Inspector2`,
  `ISessionImporter`/`ISessionExporter`, and `IHandleExecAction` aren't --
  none of the five real samples this shim was built against reference
  them. A real-world extension calling into any of those still gets this
  project's own placeholder behavior, not real data.
- **`frmViewer`'s own session list is still separate and synthetic.**
  `SessionBridgeRunner` (sibling `Clearinet.LegacyExtensionHost` project)
  talks directly to loaded `IAutoTamper` instances and never touches this
  window's session list -- `GetSelectedSessions()` still only ever returns
  the two demo sessions from `LoadDemoSessions`, even while real traffic
  is flowing through the bridge to the same extension's `IAutoTamper`
  hooks. See `frmViewer`'s own remarks.
- **`Session.utilDecodeResponse()` doesn't actually decode anything.**
  Always reports success without touching `responseBodyBytes` -- a real
  gzip/chunked-encoded response passed through the bridge gets no real
  decoding. See `Session.cs`'s own remarks.
- **`ClientChatter.FailSession(...)` is a no-op.** The wire protocol has
  no verb for "abort/fail this session" at all yet -- only the four
  `IAutoTamper` hooks' ordinary edit-and-continue shape. See
  `ClientChatter.cs`'s own remarks.
- **What doesn't round-trip through the bridge at all, per the design
  doc's own "What doesn't round-trip yet, honestly" note:** HTTP version
  (no member for it on this shim's own `Session` type), and a genuinely
  repeated header name (multiple `Set-Cookie` being the realistic case),
  which collapses to its last value rather than being preserved.
- **`frmViewer.actDoCompareSessions`** shows a placeholder message instead
  of a real diff view.
- **`Utilities.ReadSessionArchive`/`WriteSessionArchive`** are real now (see
  `SazArchive.cs`) for plain, unencrypted `.saz` archives -- but
  password-protected/encrypted ones (`CONFIG.bUseAESForSAZ` or a non-empty
  password) are explicitly refused (both throw `NotSupportedException`
  rather than silently reading nothing or writing an unprotected file).
  Real ZIP encryption/decryption wasn't implemented this pass -- see
  `SazArchive.cs`'s own remarks.
- **Only the member surface these five specific extensions touch is
  implemented.** A different real-world extension calling anything else
  on this compatible surface will fail to load (a clear, logged error, not
  a silent one -- see `LegacyExtensionLoader`) until that surface is added the same
  way: metadata-confirmed first, then implemented.
