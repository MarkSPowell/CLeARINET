# CLeARINET Interception Certificate Design

This document exists because ten source files reference "the Interception
Certificate Design doc" for design rationale that was never actually
written down anywhere accessible — only scattered across code comments.
This reconstructs what's knowable from those comments and from the
implementation itself (`src/Clearinet.ProxyCore/Certificates/`), which
already fully implements the Windows half of what's described below. Where
a decision was made but the reasoning behind it isn't recoverable from the
code, that's marked explicitly rather than guessed at.

## Why a certificate authority at all

To inspect HTTPS traffic, CLeARINET sits in the middle of the TLS
connection: it terminates TLS toward the client with its own certificate,
decrypts the traffic, and separately terminates TLS toward the real
server. For the client (a browser) to accept CLeARINET's certificate
without a warning, it needs to trust the certificate authority that
signed it — the same interception model Fiddler Classic itself uses, and
the reason every such tool has to install a root certificate at all.

## The root certificate authority (`CertificateAuthority`)

One root CA is generated per install, per user:

- **Algorithm**: ECDSA on the P-256 curve, SHA-256 signing.
- **Validity**: 5 years from one day before generation (a small
  `notBefore` buffer against clock skew).
- **Subject**: `DO_NOT_TRUST_ClearinetRoot (<machine name>, generated
  <date>)` — the `DO_NOT_TRUST_` prefix follows Fiddler Classic's own
  long-standing convention (`DO_NOT_TRUST_FiddlerRoot`), so a stale or
  foreign copy is recognizable in a trust store at a glance.
- **Key storage**: the private key is persisted with `PersistKeySet` but
  deliberately without `Exportable` — the key exists as a real key
  container on disk, but can't be exported back out through the
  certificate API once created. It's exported to an in-memory PFX only
  for that one round trip (immediately re-imported with the flags
  actually wanted), and the PFX bytes are zeroed afterward.
- **Store location**: `CurrentUser\My` (personal store) for the
  certificate-with-key, `CurrentUser\Root` (trusted root store) for trust.
  A per-user install, not per-machine — no admin elevation required, and
  no other user account on the same machine is affected.
- **Reuse across runs**: on startup, an existing, still-valid, still
  privately-keyed root already in the personal store is reused rather
  than generating a new one every launch — the same root keeps working
  across Start/Stop cycles and app restarts, so the client's trust
  decision doesn't have to happen more than once.

### Installing trust

Adding the root to `CurrentUser\Root` is what triggers Windows' native
"Do you want to install this certificate?" prompt the first time — this
is deliberate and not something CLeARINET tries to suppress or automate
around. A silent, unprompted root-CA install would be indistinguishable
from malware behavior; relying on the OS's own confirmation dialog is the
honest way to do this, and it's the same prompt experience Fiddler
Classic's own documented setup walks users through.

### Removal

`CertificateAuthority.Uninstall()` removes the root from both the
personal and trusted-root stores — the "one-click removal" mechanics this
design calls for. It's implemented but **not yet wired up to any UI or
CLI command** — there's no menu item or button that calls it yet.

## Leaf certificates (`LeafCertificateProvider`)

A fresh leaf certificate is signed for each host CLeARINET intercepts,
signed by the root above:

- **Algorithm**: ECDSA P-256 again, for consistency with the root.
- **Validity**: 7 days, `notBefore` backdated 5 minutes for clock-skew
  tolerance. Short-lived by design — these are ephemeral, per-run
  certificates, not something meant to be reused across long stretches of
  time.
- **Subject / SAN**: the certificate's Subject Alternative Name carries
  the actual host — a DNS name, or an IP address when the host being
  connected to is literally an IP. Modern clients (Chromium in
  particular) validate against the SAN, not the legacy Subject Common
  Name, so getting the SAN right is what actually matters for the
  certificate to be accepted.
- **Extended Key Usage**: server authentication only (OID
  `1.3.6.1.5.5.7.3.1`).
- **Key usage**: `DigitalSignature` only — no `KeyEncipherment`, since
  that's an RSA-specific usage that doesn't apply to an EC key.
- **Chain building**: the leaf carries an Authority Key Identifier tying
  it back to the issuing root's key, which strict validators (and some
  Chromium checks) expect even for a simple two-level chain.

### Caching and concurrency

Leaves are cached per host and reused until within 5 minutes of
expiring. The cache is a single-flight design
(`ConcurrentDictionary<string, Lazy<X509Certificate2>>` with
`ExecutionAndPublication`): when a real browser opens several parallel
connections to a host it hasn't talked to yet, all of them hit the same
uncached-host case within milliseconds of each other, and only the first
caller actually signs a certificate — the rest wait for that one result
instead of each racing to sign (and persist a key container for) the same
host redundantly. This was built specifically because that redundant-race
behavior was reproduced live as intermittent `AuthenticationException` /
`Win32Exception: An unknown error occurred while processing the
certificate` failures under concurrent signing.

A failed signing attempt evicts itself from the cache rather than caching
the failure forever (the default behavior of a faulted
`Lazy<T>` under `ExecutionAndPublication`), so a transient failure doesn't
poison every future request for that host until the process restarts.

### The ephemeral-key problem

`CertificateRequest.Create(...).CopyWithPrivateKey(...)` hands back a
certificate whose private key exists only in memory. On Windows,
`SslStream`'s server-side handshake (Schannel) refuses such a certificate
outright with "the platform does not support ephemeral keys." The fix is
the same export-then-reimport-with-`PersistKeySet` trick the root uses,
just without also adding the leaf to any visible store — only the key
needs to stop being ephemeral.

A known, tracked trade-off: this leaves a small CNG key-container
artifact behind per leaf ever generated. **Cleaning those up as leaves
expire and get evicted from the cache is a real, open follow-up — not yet
built.**

A freshly persisted key container also isn't always immediately ready for
Schannel to use — reproduced live as an occasional, isolated
`AuthenticationException` failure right in the middle of a real client's
TLS handshake, independent of the concurrency issue the single-flight
cache already guards against. `WarmUpPrivateKey` forces one real
private-key signing operation immediately after import (before the
certificate is ever handed to `SslStream`), with up to 2 attempts, so
that failure mode surfaces (and can be retried) right after import — a
place where it's cheap to reason about — rather than mid-handshake, where
it silently drops one of the client's connections.

## Platform status

**Windows**: fully implemented — this entire document describes the
Windows half of the design, and it's what's actually running today.

**macOS**: not implemented. Every reference to "the macOS gap" in the
codebase points here. The equivalent work would be: installing the root
CA into the macOS Keychain (as a trusted certificate, with the
`kSecTrustSettingsResult` policy needed for it to be honored by Chromium
and Safari) and the removal path for it. Nobody has designed this half
yet.

> **Needs your input:** the Keychain trust APIs to use (Security.framework
> via P/Invoke vs. shelling out to `security add-trusted-cert`), whether
> Keychain installation needs the user's login password the same way
> Windows' prompt needs a click, and how per-user vs. system Keychain
> should map to CLeARINET's current per-user design.

## Open risks summary

- macOS Keychain trust path — not designed, not built.
- Leaf certificate key-container cleanup on cache eviction — tracked, not
  built.
- `Uninstall()` — implemented, not wired to any UI or CLI entry point.
