# Demo 62: Require-Signed Updates

A self-contained demo of the **authoring** side of update trust: `.Integrity(...)` with a
key-epoch and a declared revocation, plus `.UpdateFeed(...)` to point the engine at an update
feed. Verifying and enforcing that config against a downloaded update bundle is a
runtime/engine concern -- this demo builds the authoring side for real and reads back what
actually landed in the shipped manifest, then narrates the runtime enforcement it feeds.

## What This Demonstrates (Authoring Side -- Actually Built and Run)

- `IntegrityBuilder.Epoch(n)` -- bumps the key-epoch, cryptographically covered by the
  signature, so a client refuses any future bundle whose epoch is lower than the highest it
  has already accepted (anti-downgrade/replay)
- `IntegrityBuilder.Revoke(fingerprint)` -- declares a retired publisher key's fingerprint
  revoked by this release; once a client applies this verified update it records the
  revocation and refuses any future bundle signed only by that key
- `BundleBuilder.UpdateFeed(feedUrl, policy)` -- configures the update feed URL and policy
  carried in the manifest
- Reading the compiled bundle's manifest back and confirming the epoch, revocation count, and
  update feed config are really embedded in the signed envelope and the manifest -- not just
  passed to the builder and silently dropped

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | Minimal MSI chain item, buildable/runnable standalone |
| `bundle/` | Bundle (EXE) | Builds its own copy of that MSI inline, signs with an epoch + revocation, configures an update feed |

## How to Run

```
dotnet run --project demo/62-require-signed-updates/bundle -- -o ./out
```

Prints the epoch, the declared revoked fingerprint, and the update feed URL/policy both as
configured and as read back from the compiled manifest.

## Key API Calls

```csharp
var bundle = new BundleBuilder()
    .Name("MyApp")
    .Version("2.0.0")
    // ...
    .Integrity(i => i
        .Epoch(2)                    // bumped because a prior key was rotated out
        .Revoke("E9065B41...4C"))    // the retired key's fingerprint
    .UpdateFeed("https://updates.example.com/feed.json", UpdatePolicy.AutoUpdate)
    .Chain(chain => chain.MsiPackage(msiPath, p => p.Id("App")))
    .Build();
```

## What This Demo Does NOT Run: Runtime Update Verification

The other half of "require-signed updates" happens inside the *already-installed* engine,
after it downloads a candidate update bundle and before it relaunches it
(`FalkForge.Engine.Integrity.StagedUpdateVerifier`). That code cannot run inside a build-time
console demo -- it needs a running, already-trusted engine process, a staged download, and a
persisted per-machine trust store. This section narrates what it does.

**Why the check has to happen in the already-trusted engine, not the downloaded one.** A
downloaded update is fetched from an attacker-controllable feed, and relaunching it with a
`--require-signed` flag proves nothing -- the downloaded artifact carries its own embedded
engine, which is free to ignore the flag. So the verification runs in the engine the user
*already* trusts, over the staged bytes, before that engine ever launches the new one.

`StagedUpdateVerifier` always verifies with `requireSigned: true` on the update path (unlike
a fresh install, where an unsigned bundle is backward-compatible). It rejects:

| Rejection | Error | Cause |
|---|---|---|
| Missing signature | `INT007` | The staged bundle carries no embedded signature at all -- an update, unlike a fresh install, must be signed |
| Untrusted / invalid / revoked signature | `INT001` | No signature both matches a fingerprint in the engine's trusted set (which excludes locally-revoked fingerprints) and cryptographically verifies |
| Downgrade or replay | `INT008` | The bundle's signed epoch is below the highest epoch this machine has already accepted -- exactly the anti-downgrade property `Epoch(n)` exists to enable |
| Tampered payload | `INT006` | A payload's bytes no longer match the hash the signature covers |

The trust set and the anti-downgrade epoch are not read from the bundle -- they come from the
engine's own baked `-p:FalkForgeTrustedKey` set (see demo 60) plus a per-machine, ACL-validated
`TrustStateStore` that persists the highest epoch accepted and any locally-applied
revocations. A revocation declared via `Revoke(fingerprint)` in *this* release, once installed,
is what causes a *future* download signed only by that now-retired key to be rejected on the
next machine that applies this update.

## Notes

- The engine-side pieces (`StagedUpdateVerifier`, `TrustStateStore`, `BundleTrustVerifier`)
  live in `FalkForge.Engine` / `FalkForge.Engine.Protocol` and are exercised by that project's
  own test suite, not by this build-time demo.
- For the ephemeral/stable-key signing basics this demo's `.Integrity(...)` call builds on,
  see demo 59. For dual-sign rotation and the trusted-key/roles model the runtime check above
  consults, see demo 60.
