# Demo 62: Require-Signed Updates

A self-contained demo of the **authoring** side of update trust: `.Integrity(...)` with a
key-epoch and a declared revocation, plus `.UpdateFeed(...)` to point the engine at an update
feed. Verifying and enforcing that config against a downloaded update bundle is a
runtime/engine concern -- this demo builds the authoring side for real and reads back what
actually landed in the shipped manifest, then narrates the runtime enforcement it feeds.

## What This Demonstrates (Authoring Side -- Actually Built and Run)

- `IntegrityBuilder.Epoch(n)` -- adds a key epoch that is cryptographically covered by the signature
- `IntegrityBuilder.Revoke(fingerprint)` -- adds a retired publisher key's fingerprint to the signed revocation metadata
- The current boundary: this metadata is verified, but the production bootstrapper does not persist accepted trust state yet, so cross-run anti-downgrade and revocation enforcement is dormant
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

`StagedUpdateVerifier` verifies with `requireSigned: true` on the update path (unlike a fresh install, where an unsigned bundle is backward-compatible). Signature and payload checks are active. The epoch and locally persisted revocation checks below only become effective when a host loads and saves `TrustState`; the production bootstrapper does not do that yet.

| Rejection | Error | Cause |
|---|---|---|
| Missing signature | `INT007` | The staged bundle carries no embedded signature at all -- an update, unlike a fresh install, must be signed |
| Untrusted / invalid / revoked signature | `INT001` | No signature both matches a fingerprint in the engine's trusted set (which excludes locally-revoked fingerprints) and cryptographically verifies |
| Downgrade or replay | `INT008` | The bundle's signed epoch is below the highest epoch this machine has already accepted -- exactly the anti-downgrade property `Epoch(n)` exists to enable |
| Tampered payload | `INT006` | A payload's bytes no longer match the hash the signature covers |

The verifier accepts an explicit trust set and `TrustState`, and `TrustStateStore` can persist the highest accepted epoch and locally applied revocations in an ACL-protected location. Those components are tested, but the production bootstrapper currently uses default in-memory state and never saves it. As a result, `INT008` and cross-run revocation enforcement are not active in shipped bundle execution yet.

## Notes

- The engine-side pieces (`StagedUpdateVerifier`, `TrustStateStore`, `BundleTrustVerifier`)
  live in `FalkForge.Engine` / `FalkForge.Engine.Protocol` and are exercised by that project's
  own test suite, not by this build-time demo.
- For the ephemeral/stable-key signing basics this demo's `.Integrity(...)` call builds on,
  see demo 59. For dual-sign rotation and the trusted-key/roles model the runtime check above
  consults, see demo 60.
