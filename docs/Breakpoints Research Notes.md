# Breakpoints Research Notes

Referenced from `BreakpointAbortedException.cs` as "the breakpoints
research notes" — informal research into how Fiddler Classic's own
breakpoint system behaves, done against the public [Fiddler Classic
docs](https://github.com/telerik/fiddler-docs) (per the clean-room policy
in `CONTRIBUTING.md` — no decompiled or leaked source was involved). Like
the other docs in this folder, the notes themselves were never written
down anywhere accessible; this collects what's actually recoverable from
code comments that cite them. This is a short document because the
research it points back to was narrow — expand it here as more of
Fiddler's breakpoint behavior gets researched for future work.

## QuickExec command mapping

`BreakpointRules` mirrors Fiddler Classic's breakpoint commands, one
active value per command rather than a general rule-list engine (matching
how Fiddler itself works — one active value per command, not a list):

| CLeARINET | Fiddler QuickExec | Behavior |
|---|---|---|
| `BreakOnAllRequests` | `Rules > Automatic Breakpoints > Before Requests` | Pause every request |
| `BreakOnAllResponses` | `Rules > Automatic Breakpoints > After Responses` | Pause every response |
| `RequestUrlContains` | `bpu` | Pause a request whose target contains a substring |
| `ResponseUrlContains` | `bpafter` | Pause a response whose *request* target contains a substring |
| `RequestMethodEquals` | `bpm` | Pause a request with a specific HTTP method |
| `ResponseStatusCodeEquals` | `bps` | Pause a response with a specific status code |

The last four are implemented in `Clearinet.ProxyCore.Breakpoints.BreakpointRules`
but — see the Fiddler Feature Inventory doc — not yet exposed anywhere in
the desktop UI, which only wires up the two "break on all" toggles today.

## Resume vs. Abort

Fiddler Classic's own documentation doesn't describe a distinct "abort" a
paused session, as an action separate from simply not resuming it. This
research treated that as ambiguous enough to warrant an explicit design
decision rather than silently guessing: CLeARINET makes Abort a first-class
action (`PendingBreakpoint.Abort`, surfaced as its own command in the UI),
distinct from Resume, and both are handled through the same
`BreakpointAbortedException` path a killed session would otherwise reach —
so the two "give up on this session" paths converge on one outcome instead
of behaving differently by accident.

## Editing model

Fiddler Classic's TextView shows a paused message as one editable blob of
raw text (start line, headers, and body together) rather than separate
controls per field. `PendingBreakpoint.RawText` and `HttpMessageText`
follow that same model — see `HttpMessageText`'s own doc comment for how
CLeARINET deliberately differs from Fiddler Classic here: an edit that
leaves the raw text malformed is reported back as an error rather than
being silently dropped when the person tries to resume, which is a
documented Fiddler Classic gotcha this design avoids.

> **Needs your input:** this document is thin. If there's more research
> behind other breakpoint decisions — why 5 minutes of leaf-certificate
> validity headroom, why breakpoints pause before a `Session` object even
> exists (see `BreakpointStage`'s own doc comment), or anything else
> pulled from the Fiddler Classic docs during design — it belongs here so
> the next person doesn't have to redo that research from scratch.
