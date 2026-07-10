# Demo 63: Hybrid Post-Quantum Signing (ML-DSA / FIPS 204)

A self-contained demo of hybrid post-quantum bundle-integrity signing: the manifest is signed
by **both** a classical ECDSA-P256 key and an ML-DSA-65 (FIPS 204) companion key, and the
engine-side companion pin makes stripping the post-quantum signature a hard failure (INT011).

## Why hybrid?

A sufficiently capable quantum computer breaks ECDSA-P256 — an attacker could then forge the
classical signature on a malicious bundle. ML-DSA (the NIST-standardized lattice scheme) is
believed to resist quantum attack, but is younger than ECDSA. Hybrid signing takes the
conservative AND: a bundle counts as trusted only when **both** signatures verify, so it stays
sound as long as *either* algorithm stands.

## The anti-strip guarantee (the part that actually matters)

Carrying two signatures is worthless if an attacker can simply delete the post-quantum one and
present the (still valid) classical signature alone. FalkForge closes this the same way it
closes every self-describing-bundle hole: **the bundle proves, the binary decides.**

- The fact "this publisher is hybrid" is pinned **in the verifying engine** — via
  `EngineTrustAnchor.TrustHybridKey(classicalSpki, pqSpki)` in code, or the baked trusted-key
  item metadata in the engine's build:

  ```xml
  <FalkForgeTrustedKey Include="<classical fp, 64 hex>"
                       Roles="release"
                       PqFingerprint="<ML-DSA fp, 64 hex>"
                       PqAlgorithm="ML-DSA-65" />
  ```

  (The property short-form `-p:FalkForgeTrustedKey=<fp>` stays classical-only; hybrid pinning
  uses the item form or code registration.)
- Nothing in the bundle declares the companion, so nothing in the bundle can un-declare it.
  A stripped envelope leaves a classical signature whose pinned ML-DSA companion cannot be
  satisfied → the engine rejects with **INT011**.
- A trusted classical key *without* a pinned companion verifies exactly as before — hybrid
  rollout is per-key, fully backward compatible.

## What This Demonstrates

- `BundleBuilder.Integrity(i => i.HybridKey(classicalPem, pqPem))` — one fluent call authors
  the dual-signed bundle
- Reading the compiled bundle's envelope back: two signature entries over the same signed
  bytes, classical first, the ML-DSA entry tagged `"algorithm": "ML-DSA-65"`
- Verifying with the real envelope verifier (`IntegrityEnvelopeCodec.MatchTrustedSignature`)
  under a companion pin (`PqCompanionPolicy`) — passes
- The strip attack: the same envelope with the ML-DSA entry removed is rejected with INT011,
  even though its classical signature is still cryptographically valid

The same hybrid bundle can be produced from a `forge build` JSON config:

```json
"signing": {
    "provider": "pem",
    "keyPath": "keys/release.pem",
    "pqKeyPath": "keys/release-mldsa.pem"
}
```

(`pqKeyEnv` sources the ML-DSA PEM from an environment variable instead; the PQ key follows
the same secret rules as the classical key — file path or env var NAME, never inline material,
fail-closed on an unset variable.)

## Project Structure

| Sub-project | Type | Description |
|---|---|---|
| `msi-package/` | MSI | A minimal MSI package shape, buildable and runnable on its own |
| `bundle/` | Bundle (EXE) | Builds its own copy of that MSI inline into a temp directory, then compiles the hybrid-signed bundle and runs the verify + strip-attack proof |

## How to Run

```
dotnet run --project demo/63-hybrid-pq-signing/bundle -- -o ./out
```

This prints the hybrid identity (both fingerprints), compiles the dual-signed bundle, lists
the envelope's two signature entries, verifies it companion-pinned (passes), then strips the
ML-DSA entry and verifies again (rejected with INT011).

**OS requirement:** ML-DSA needs a current Windows 11 build (the PQC CNG additions). On an
older OS `MLDsa.IsSupported` is false and the demo fails loud up front with SGN011 — the same
fail-loud rule a configured production build follows (post-quantum signing has no silent
fallback on the build machine). Verification-side OS incapability is a separate, engine-side
policy: an engine that cannot verify ML-DSA accepts a hybrid-pinned key on its classical
signature with a loud log, because OS capability is not attacker-controllable.

## Scope (honest)

Like the other signing demos, this demo exercises the **manifest-signing and verification**
layer end-to-end with the real codec and the real companion rule — it does not run an actual
installation. The compiled bundle wraps the design-time engine stub, so treat it as a signing
artifact, not a distributable installer. The runtime gates (`BundleTrustGate`,
`PayloadIntegrityGate`, `StagedUpdateVerifier`) enforce exactly the companion rule shown here,
via the pins registered in `EngineTrustAnchor`.

## Notes

- **PQ-only is rejected by design.** An ML-DSA entry is a *companion* to a classical identity,
  never a trust anchor on its own — a PQ-only envelope could never verify on any engine, so
  the build fails loud (SGN012) instead of emitting one. `HybridKey` requires both halves.
- **Zero-config builds are hybrid too:** `.Integrity(i => { })` emits an ephemeral classical +
  ML-DSA pair (when the build OS supports ML-DSA), so the dev loop exercises the production
  envelope shape.
- **Quorum:** a hybrid pair is ONE signer. The companion is a validity condition on the
  classical entry, not an extra quorum vote — one person holding one hybrid pair can never
  fill two slots of a 2-distinct rule (see demo 60 for roles/quorum).
- **Rotation:** `HybridKey` is repeatable for rotation-safe dual-sign of hybrid pairs, exactly
  like `AddSigningKey` for classical keys (demo 60).
- For classical integrity signing basics see demo 59; trusted-key pinning and rotation, demo
  60; remote signing, demo 61; runtime update-trust enforcement, demo 62.
