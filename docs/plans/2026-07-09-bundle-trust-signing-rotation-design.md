# Bundle Payload Trust, Signing & Rotation Design (assessment item C14)

Status: **DRAFT 2026-07-09.** Scope: close the bundle payload-trust critical (two remaining RCE bypasses after commit `6dd9710` on `fix/bundle-payload-trust`) and replace the single self-describing-key signature with a rotation-safe, trusted-set multi-signature scheme. Design only — no code in this document. Implementation is split into Stage 1 and Stage 2 (see §9).

This document is the authoritative design for the trust model. It supersedes the "authorship requires an out-of-band host pin" seam described in `docs/provenance.md §3`, which is the mechanism this work replaces with a baked-in trusted key set.

> **Stage 1 landed (2026-07-09, `fix/bundle-payload-trust`).** The multi-signature v2 envelope, the baked trusted-set pin, and verify-any are implemented; the re-sign bypass (B1) is closed **when a publisher bakes a key**. C14 remains **open** (critical not fully closed until Stage 2 ships require-signed on the update path + epoch/revocation store + the `forge extract`/`forge migrate` gates). Stage 1 deviations from the literal design, each deliberate:
> - **No `epoch` field yet.** Per §4.3 the signed message stays `ComputeSignedBytes(files)` (files-only), byte-identical across v1/v2. The `epoch` envelope field and the epoch-inclusive signed-bytes change (§6.3) are Stage 2 — shipping an unsigned, unenforced epoch in Stage 1 would be dead config, and §4.3/§6.3 conflict on this point (§4.3 chosen for Stage 1). Stage 2 must handle the signed-bytes transition (gate epoch inclusion on presence, or bump the version) so Stage-1 v2 bundles still verify.
> - **`TrustPolicy`** carries `TrustedFingerprints` + `RequireSigned` only; `trustStorePath` (§6.4) is Stage 2. It lives in `FalkForge.Engine` (not readable from `Engine.Protocol`), so `SignedPayloadTocVerifier` takes the raw `(IReadOnlySet<string>, bool requireSigned)` rather than the struct.
> - **`RequireSigned`** is implemented at both gates (unsigned → INT007) and unit-tested, but never wired true in Stage 1 (fresh install always passes unsigned). The update-path wiring is Stage 2.
> - **Signer/builder:** multi-key dual-sign (`AddSigningKey`/`SigningKeys`, `IntegrityConfiguration.SigningKeyPaths`) shipped; `Epoch`/`Revoke` builder methods deferred to Stage 2 with the store.
> - **Error taxonomy:** INT001/002/003/004/006/007 now use `ErrorKind.IntegrityError` (§5.5); `SecurityError` retained for path-traversal and the SGN002 build-time key-load failure.
> - **Empty baked set = consistency-only** (§5.1): an engine built with no `FalkForgeTrustedKey` accepts any self-verifying signature (pre-pin behavior, safe for a user-chosen fresh install); it is Stage 2's require-signed that makes the update path fail closed. The MSBuild injection is verified via `dotnet build -p:FalkForgeTrustedKey=…` + inspecting the generated `TrustedKeys.g.cs`; the committed test asserts the default set is empty, non-null, and reachable.
> - **Unsigned fresh-install warning** (§5.2 step 2 / §3.4) is not emitted — the gate returns a `Result` with no logger; wiring the `IFalkLogger` warning is deferred. `docs/provenance.md §3` (host-pin seam, INT005 retirement, INT007) is not yet updated — a docs follow-up.

---

## 1. Problem statement

### 1.1 The critical

A bundle EXE is `[PE stub][Magic][Manifest JSON][compressed payloads][TOC][Footer]`. The manifest carries an ECDSA-P256 signature envelope over the per-package payload hashes (`InstallerManifest.ManifestSignature`, `InstallerManifest.cs:27`). The TOC (table of contents) and the payloads are appended to the overlay *after* the PE stub is signed, so Authenticode (when present) covers only the stub, never the overlay — stated in `docs/provenance.md §3` "Payload byte binding" and enforced by construction via `BundleDetacher`.

Runtime extraction verifies each payload's decompressed bytes against the **TOC** hash (`TocEntry.Sha256Hash`, or `TocEntry.ReconstructedSha256Hash` for a reconstructed delta) — an unsigned value in the overlay. Commit `6dd9710` added `SignedPayloadTocVerifier` (`src/FalkForge.Engine.Protocol/Integrity/SignedPayloadTocVerifier.cs`) which binds, before extraction, every TOC payload in the signed set to the ECDSA-signed `PackageInfo.Sha256Hash`. That closes the "flip bytes, rewrite the TOC hash, leave the signature untouched" hole. It does **not** close the two bypasses below, both of which defeat the signature itself rather than the TOC binding.

### 1.2 B1 — self-describing key, no trust anchor (re-sign attack)

The ECDSA envelope embeds its **own** verifying public key:

- `ManifestSignatureEnvelope.cs:24-25` — `[JsonPropertyName("publicKey")] public string PublicKey`.
- `IntegrityEnvelopeCodec.VerifySignature` (`IntegrityEnvelopeCodec.cs:104-110`) imports that embedded key and verifies against it: `ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _); return ecdsa.VerifyHash(hash, signatureBytes);`.

Nothing anchors that key to a trusted publisher. An attacker generates a fresh free P-256 key, tampers the payloads, recomputes all hashes (manifest + envelope + TOC), re-signs the file list with their key, and embeds their public key. `VerifySignature` passes, `PayloadIntegrityGate` passes, `SignedPayloadTocVerifier` passes (all three hashes agree internally). The payload runs. This is honest self-describing-key mode — "tamper-evidence, not authorship" as `PayloadIntegrityGate.cs:22-27` documents.

The intended mitigation exists only as a **dead field**. `PayloadIntegrityGate.Verify` accepts an `expectedPublisherKeyFingerprint` (`PayloadIntegrityGate.cs:62-64`) and `PipelineContext.ExpectedPublisherKeyFingerprint` (`PipelineContext.cs:106`) feeds it at `ApplyStep.cs:66-67`. But that property is **never assigned anywhere in `src/`** (a repo-wide search for `ExpectedPublisherKeyFingerprint =` finds only a documentation sample in `docs/provenance.md:214`). It is always `null`, so the pin check at `PayloadIntegrityGate.cs:85` is always skipped. Worse, `SignedPayloadTocVerifier` — the gate that runs on the self-extract and bootstrapper paths (`Program.cs:341`, `Program.cs:442`) — has **no pin parameter at all**.

### 1.3 B2 — no signature required (strip attack)

Both gates pass through when there is no signature:

- `PayloadIntegrityGate.cs:66-67` — `if (manifest.ManifestSignature is null) return Result<Unit>.Success(default);`
- `SignedPayloadTocVerifier.cs:56-58` — `if (manifest.ManifestSignature is null) return Unit.Value;`

An attacker takes any signed bundle, sets `ManifestSignature` to `null` (and drops the envelope), tampers payloads, and rewrites the TOC hashes to match. Both gates see an "unsigned" bundle and wave it through with only TOC-level self-consistency (which the attacker also controls). Documented as a deliberate backward-compat trade-off in `docs/provenance.md §3` "Unsigned manifests pass" — but on the **update path** it is a straight RCE: the engine will download, verify-nothing, and execute an attacker's replacement.

The build-time signer can also be told to skip signing via the `FALKFORGE_NO_SIGN` env var (`BundleIntegritySigner.cs:31-32`) — not an attack, but it means "signed" is not currently an invariant even for first-party builds.

### 1.4 Two extraction paths bypass the binding entirely

`SignedPayloadTocVerifier.Verify` is called on the engine's own extract/bootstrap paths (`Program.cs:341`, `442`) but **not** by:

- `forge extract` — `ExtractCommand.ExtractBundle` (`ExtractCommand.cs:72-153`) calls `BundleReader.Extract` then `BundleReader.ExtractPayloadToFile` per entry with only per-file SHA-256 (TOC) verification and zip-slip containment. No trust binding.
- `forge migrate` — `MigrationProjectGenerator` (`MigrationProjectGenerator.cs:257`, `271`) calls `BundleReader.Extract` / `BundleReader.ExtractPayload` to reconstruct a project, again with no trust binding.

Neither *executes* the payload, so these are lower severity than B1/B2, but they extract attacker-controlled bytes as if trusted. Stage 2 routes both through the gate.

### 1.5 Summary of the gap

The signature proves internal consistency and (with `SignedPayloadTocVerifier`) binds bytes → TOC → signed hash, but **any party can produce a valid signature** (B1) and **no signature is required** (B2). Trust is therefore currently zero on the update path. This design adds the missing trust anchor (a key set baked into the engine) and makes signatures mandatory where it matters (updates).

---

## 2. Threat model

### 2.1 Attacker capabilities (in scope)

- **Full artifact rewrite.** The attacker can hand the user a completely rewritten installer EXE: any payload bytes, any manifest, any TOC, any signature envelope, signed with any key they choose. Nothing *inside the bundle* is trustworthy.
- **Update-feed MITM / feed poisoning.** The attacker controls the bytes returned from the update feed URL and payload URLs (`UpdateDownloader.cs` downloads by URL + expected SHA, but the expected SHA itself comes from the attacker-controlled feed/manifest). They can substitute a fully re-signed malicious bundle for a legitimate update.
- **Replay / downgrade.** The attacker can serve an older, still-validly-signed bundle (including one signed by a key that has since been revoked) in place of the current release.

### 2.2 Attacker limitations (out of scope — assumed hard)

- **Cannot extract the publisher's private signing key** (held in a vault / CI secret, never shipped).
- **Cannot alter the shipped engine binary's baked-in trusted key set.** The engine EXE (`FalkForge.Engine.exe`, NativeAOT) that is already installed / already running is trusted. For a fresh install, the engine that runs *is the one embedded in the bundle the user chose to run* — trusting that engine's baked pin is equivalent to trusting the bundle they already chose to execute (see §3.3). We do not defend a user who runs an attacker's engine build; that is outside any signature scheme.
- **Cannot forge ECDSA-P256 / SHA-256.** Standard cryptographic assumption.

### 2.3 Out of scope for this design

- Authenticode / paid code-signing certificates (separate, optional layer; already partially wired for the *update launcher* via `UpdatePublisherThumbprint` + `IAuthenticodeValidator`, but it cannot protect overlay payloads).
- Protecting `forge extract` / `forge migrate` *bytes at rest* beyond routing them through the same verify gate (they don't execute).
- OTA delivery of a new trusted set (seam designed in §6, delivery deferred — §10).
- "Require-signed everywhere" (fresh install of an unsigned bundle stays allowed-but-warned — §3.4).

---

## 3. Trust model

### 3.1 Trusted key SET, pinned in the engine

The engine holds a **set** of trusted publisher key fingerprints (SHA-256 of each key's `SubjectPublicKeyInfo`, uppercase hex). A signature is trusted iff it verifies against a key whose fingerprint is in this set. A set (not a scalar) is mandatory from day one so key rotation never requires a flag-day engine change (§7).

The set is **baked into the engine binary at build time** — the one channel the attacker cannot rewrite (§2.2). It is not read from the bundle, not from the manifest, not from a config file next to the EXE.

### 3.2 How the set is injected at build time

The engine is built **per publisher** (each publisher produces their own `FalkForge.Engine.exe` with their own trusted set), so injection is an MSBuild input to the `FalkForge.Engine` project:

- An MSBuild property / item, e.g. `<FalkForgeTrustedKey Include="A1B2C3..." />` (one item per fingerprint, supporting a set), defaulted empty.
- A build target generates a `TrustedKeys.g.cs` into `$(IntermediateOutputPath)` containing a `static class BakedTrustedKeys { public static readonly string[] Fingerprints = { ... }; }` (or a `FrozenSet<string>` per Gate 6). This mirrors the existing generated-source precedent in the SDK: `Sdk.targets:43-44` already uses a `RoslynCodeTaskFactory` inline task (`_WriteFalkProjectOutputsSource`) to emit `ProjectOutputs.g.cs` (`Sdk.targets:136`). We reuse that idiom inside the Engine project's build rather than inventing a new one.
- Alternative considered: an embedded resource (a `trusted-keys.txt` `EmbeddedResource`). Rejected for the hot path — a generated `FrozenSet<string>` constant is allocation-free at read time and AOT-trivial, whereas resource loading adds a stream read. The generated-constant approach is chosen.
- CI wiring: the publisher's release pipeline passes `-p:FalkForgeTrustedKey=<fingerprint>` (repeatable) from the same secret store that holds the signing key (§8). A build with an empty set produces an engine that trusts nothing — every signed bundle fails require-signed on the update path, and unsigned bundles still run with a warning (§3.4). That is a safe default (fails closed on updates).

The fingerprint is a public value (hash of a public key); baking it in leaks nothing.

### 3.3 Fresh-install self-consistency

For a fresh install there is no rotation problem: the bundle embeds the engine, and that engine was built by the same publisher with the same trusted set that includes the key that signed that very bundle. So a first-party fresh install is self-consistent **by construction** — the baked set always contains the signing key for the release it ships in. The user's trust decision is "do I run this EXE at all"; once made, the embedded engine's pin is exactly as trustworthy as that decision. Rotation (§7) therefore only ever concerns the **update path**, where an *already-installed* engine (old trusted set) must accept a *newly-signed* bundle (possibly a new key).

### 3.4 Fresh install vs update — the policy seam

Two policies, one config seam (`TrustPolicy`, §6.4), tightenable later to "everywhere":

- **Fresh install (RunAsBootstrapper / `--extract` of the embedded self):** an unsigned bundle still runs (backward compat with pre-`Integrity()` bundles), but the engine **records and warns** (structured log via `IFalkLogger`, `IntegrityError`-tagged warning event — not a failure). A signed bundle whose signatures do not verify against the baked set is **rejected** (a present-but-untrusted signature is an attack signal, not a legacy bundle).
- **Update path (downloaded update bundle):** **require-signed.** An update whose manifest has no signature, a stripped signature, or no signature that validates against the baked trusted set is **rejected** (`IntegrityError`), the update is discarded, and the running install continues on the current version. This defeats B2 on the path that matters and B1 (a re-signed update uses an untrusted key → not in the set → rejected).

Rationale for the asymmetry: a fresh install is a deliberate user act on a chosen artifact; an update is an automatic, unattended replacement of already-trusted software — the higher-risk path gets the stricter rule. The seam allows a future host policy of "require-signed on fresh install too."

---

## 4. Signature format change — single → list

### 4.1 Current shape (v1, single signature)

`ManifestSignatureEnvelope` (`ManifestSignatureEnvelope.cs`) has one `publicKey` + one `signature` over the `files` array. Serialized AOT-safe via `IntegrityEnvelopeJsonContext` (`IntegrityEnvelopeJsonContext.cs`), stored as a JSON string in `InstallerManifest.ManifestSignature`.

### 4.2 Target shape (v2, signature list)

The envelope keeps the shared, signed `files` list once (all signers sign the identical file set), and carries a **list of signatures**, each self-describing its key:

```
ManifestSignatureEnvelope (v2)
  version:    int            // bumped to 2
  algorithm:  string         // "ECDSA-P256" (unchanged; per-signature override reserved)
  files:      ManifestFileEntry[]      // unchanged; the signed message is SHA-256(UTF-8(JSON(files)))
  signatures: SignatureEntry[]         // NEW — one or more
  epoch:      int            // NEW — key-epoch counter (see §6); 0 if unset

SignatureEntry (new type)
  keyId:       string        // stable short label for the signing key (e.g. "falkforge-2026-a"); operator-facing, not trusted
  fingerprint: string        // SHA-256(SubjectPublicKeyInfo), uppercase hex — the value matched against the baked set
  publicKey:   string        // base64 SubjectPublicKeyInfo (self-describing, as today)
  signature:   string        // base64 ECDSA signature over SHA-256(UTF-8(JSON(files)))
```

`fingerprint` is included explicitly (rather than always recomputed from `publicKey`) so the verifier can index/short-circuit by fingerprint; it is **re-derived and checked against `publicKey` during verify** (a lying fingerprint that doesn't match its own key is rejected — it must never be trusted as-is). The signed message stays `SHA-256(UTF-8(JSON(files)))` exactly as `IntegrityEnvelopeCodec.ComputeSignedBytes` computes it (`IntegrityEnvelopeCodec.cs:30-35`), so every signer signs identical bytes and the canonical-bytes computation is unchanged.

### 4.3 AOT serialization impact

- Add `[JsonSerializable(typeof(SignatureEntry))]` and `[JsonSerializable(typeof(IReadOnlyList<SignatureEntry>))]` to `IntegrityEnvelopeJsonContext` (`IntegrityEnvelopeJsonContext.cs:10-11`). Source-gen only — no reflection, AOT-safe.
- `ComputeSignedBytes` is unchanged (it serializes only `files`), so the signed-byte contract is byte-identical across v1/v2 — a v2 bundle's `files` hash is computed the same way a v1 bundle's is. This is the property that makes backward compatibility clean.
- `InstallerManifest.ManifestSignature` stays a `string?` (JSON blob) — no new manifest field, no `LayoutJsonContext` change for the manifest itself. Only the envelope's internal JSON grows.

### 4.4 Backward compatibility — reading already-shipped v1 bundles

Already-signed v1 bundles exist in the field and must still verify. The codec reads **both shapes** off the same JSON:

- `IntegrityEnvelopeCodec.Parse` inspects `version`. For `version >= 2` it deserializes the v2 shape (`signatures[]`). For `version == 1` (or a missing `signatures` array with a top-level `publicKey`+`signature`) it deserializes the v1 shape and **adapts it to a one-element `signatures` list** in memory: `signatures = [ { keyId: "legacy", fingerprint: SHA-256(publicKey), publicKey, signature } ]`. From that point the verifier only ever sees a list.
- Both `[JsonSerializable]` contexts (v1 fields + v2 fields) coexist; because v1 and v2 share `version`, `algorithm`, `files`, a tolerant DTO with **both** `publicKey`/`signature` (v1) *and* `signatures`/`epoch` (v2) optional properties can deserialize either, and the adapter normalizes. Declaration order and names of the v1 properties are **frozen** (they are part of the signed-nothing envelope wire form, and `files` entry names/order remain part of the signed bytes — `ManifestFileEntry.cs` comment).
- **Trust of a v1 bundle:** a v1 bundle contributes exactly one signature entry, whose fingerprint must be in the baked trusted set to be trusted on the update path. In practice the publisher's original single key belongs in the set, so legacy bundles that were signed by that key still verify. A v1 bundle signed by an attacker's key fails the set check exactly as a v2 one does. So backward-compat does **not** reopen B1: it re-uses the same trusted-set rule.

### 4.5 Build-time production of v2

`EcdsaManifestSigner.Sign` (`EcdsaManifestSigner.cs:31-47`) currently signs once and returns a v1 envelope. In v2 it accepts **one or more** keys (§7 dual-sign) and produces a `signatures[]` with one entry per key, all over the same `files`. `BundleIntegritySigner.SignAndEnrich` (`BundleIntegritySigner.cs:23-57`) is unchanged in structure — it still calls the signer and stores the envelope JSON in `ManifestSignature`. `IntegrityConfiguration` (`IntegrityConfiguration.cs`) and `IntegrityBuilder` (`IntegrityBuilder.cs`) gain a way to supply multiple keys (§7.4).

---

## 5. Verify algorithm

Two gates share one core routine, `VerifyTrusted(envelope, trustedSet)`, added to `IntegrityEnvelopeCodec` (so both sides compute it identically — the file's existing rationale, `IntegrityEnvelopeCodec.cs:7-16`).

### 5.1 Core: `VerifyTrusted(envelope, trustedFingerprints)` → Result

1. Parse the envelope (v1 or v2, §4.4). Parse failure → `IntegrityError` **INT003**.
2. Require at least one `SignatureEntry`. Empty list → **INT003** (malformed).
3. Compute `signedBytes = ComputeSignedBytes(envelope.Files)` and `hash = SHA-256(signedBytes)` **once**.
4. For each `SignatureEntry`:
   a. Recompute `fp' = SHA-256(SubjectPublicKeyInfo(publicKey))`; if `fp' != entry.fingerprint` → skip this entry (self-inconsistent; never trust a lying fingerprint). Do not fail the whole envelope on one bad entry — another entry may be valid.
   b. If `entry.fingerprint` is **not** in `trustedFingerprints` → skip (untrusted key). *(When `trustedFingerprints` is empty, i.e. no baked set / consistency-only mode, treat every fingerprint as acceptable for step b — this preserves the current internal-consistency behavior for callers that pass no set; the require-trust decision is the caller's, §5.4.)*
   c. Import `publicKey`, `VerifyHash(hash, entry.signature)` (as `IntegrityEnvelopeCodec.cs:104-110`). On success → **accept immediately** (first valid trusted signature wins) and return success carrying the matched fingerprint + envelope.
5. No entry both matched a trusted fingerprint and verified → **INT001** ("no trusted signature validates; the bundle may be tampered or signed by an untrusted publisher").

**Accept-any semantics:** ANY signature that (verifies) AND (matches ANY pinned trusted fingerprint) accepts. This is the rotation-safe rule — during dual-sign overlap a bundle carries old+new signatures and an engine trusting either key accepts (§7).

### 5.2 `PayloadIntegrityGate.Verify` (Apply phase) — updated

Replace the current body (`PayloadIntegrityGate.cs:62-123`) with:

1. Read `TrustPolicy` + baked trusted set from context (no longer the dead `expectedPublisherKeyFingerprint` string — that parameter is removed; §9).
2. If `manifest.ManifestSignature is null`:
   - Fresh-install policy → success **but emit an `IntegrityError`-tagged warning** ("unsigned bundle; running without publisher verification"). (Today it silently succeeds, `PayloadIntegrityGate.cs:66-67`.)
   - Require-signed policy → **INT007** (new code: "signature required but absent").
3. Else run `VerifyTrusted(envelope, bakedSet)`. Failure → propagate (**INT001/003**).
4. Epoch anti-downgrade check (§6.3): reject if `envelope.epoch < storedEpoch` → **INT008**.
5. Keep the existing two-direction coverage checks (`PayloadIntegrityGate.cs:92-120`), unchanged:
   - **signed → manifest:** every signed `files` entry binds to a manifest package with matching `Sha256Hash` (**INT002**).
   - **manifest → signed (set coverage):** every `manifest.Packages` id is in the signed `files` set (**INT004**).
6. Success.

### 5.3 `SignedPayloadTocVerifier.Verify` (extraction time) — updated

`SignedPayloadTocVerifier` (`SignedPayloadTocVerifier.cs`) currently only calls `IntegrityEnvelopeCodec.VerifySignature(envelope)` (self-describing, no trust — `SignedPayloadTocVerifier.cs:67`). Update it to take the trusted set + policy and call `VerifyTrusted` instead:

1. Signature-presence policy check identical to §5.2 step 2 (unsigned → warn on fresh-install, reject on require-signed).
2. `VerifyTrusted(envelope, bakedSet)` — replaces the untrusted `VerifySignature` call at `SignedPayloadTocVerifier.cs:67`. Failure → **INT001/003**.
3. Epoch anti-downgrade (§6.3) → **INT008**.
4. Existing byte-binding loop (`SignedPayloadTocVerifier.cs:79-100`) unchanged: for every TOC entry in the signed set, `boundHash` (`ReconstructedSha256Hash` for delta, else `Sha256Hash`) must equal the signed hash, else **INT006**.

### 5.4 Coverage extension — UI/engine payloads (Stage 2)

Both gates today deliberately skip TOC payloads with **no matching signed package** — the bundle's own UI/engine infrastructure binaries (`SignedPayloadTocVerifier.cs:39-42`, `79-84`; `PayloadIntegrityGate` coverage only over `manifest.Packages`). That is a real residual hole: an attacker can tamper the UI EXE or the engine's own auxiliary payloads (which *do* execute) because they are outside the signed set.

Stage 2 extends the signed `files` set to include **every executed payload**, i.e. the UI executable and any engine-side payloads, not just chained install packages. Concretely: the build-time signer enumerates all TOC-producing payloads (from `PayloadEntry`), not just `manifest.Packages`, when building `files`. The verify coverage check (§5.2 step 5b) then asserts **every TOC entry that will be extracted/executed** is in the signed set — turning the current "skip if unmatched" into "reject if unmatched **and** executable." Non-executed data payloads (if any) may stay exempt via an explicit allowlist, but the default is: everything the bundle runs is signed.

### 5.5 Fail-loud error taxonomy

All failures use `ErrorKind.IntegrityError` (`ErrorKind.cs:32`; today these paths use `SecurityError` — migrate the integrity codes to `IntegrityError` for a precise taxonomy, keeping `SecurityError` for path-traversal/elevation). Codes:

| Code | Meaning | Where |
|------|---------|-------|
| INT001 | No trusted signature validates (tamper or untrusted publisher) | `VerifyTrusted` step 5 |
| INT002 | Signed hash ≠ manifest package hash, or signed entry with no package | Gate coverage dir 1 |
| INT003 | Malformed envelope (parse fail, empty signatures, bad base64) | `VerifyTrusted` steps 1-2 |
| INT004 | Manifest package not covered by the signed set | Gate coverage dir 2 |
| INT006 | TOC hash ≠ signed hash (post-signing overlay tamper) | TOC binding |
| **INT007** | **Signature required (update path) but absent/stripped** | policy step 2 (NEW) |
| **INT008** | **Envelope epoch < stored epoch (downgrade/replay of revoked-key release)** | epoch check (NEW) |

INT005 (host publisher-pin mismatch) is **retired** — the dead host-pin mechanism it belonged to is replaced by the baked set. Update `docs/provenance.md` error table accordingly.

---

## 6. Key epoch / revocation store

### 6.1 Purpose

The baked set says *which keys are trusted*. It cannot, on its own, stop a **downgrade/replay**: an attacker serves an older bundle signed by a key that was later compromised and removed from *newer* engines — but the *installed* engine's baked set (from when that key was still trusted) still accepts it. A persisted, monotonically-advancing **epoch** on the client fixes this: once the client has seen epoch N, it refuses any bundle with epoch < N.

### 6.2 Store shape and location

A small AOT-safe JSON file, per-machine, written by the engine:

- **Path:** `%ProgramData%\FalkForge\Trust\trust-state.json` — i.e. `Path.Combine(Environment.GetFolderPath(SpecialFolder.CommonApplicationData), "FalkForge", "Trust")`, matching the existing per-machine cache root convention (`CacheLayout.cs:13-17`). Per-machine so a per-user attacker cannot roll it back; ACL'd to admins-write (the store is only advanced during an elevated update apply).
- **Shape:**

```
TrustState
  schemaVersion: int             // store format version
  epoch:         int             // highest key-epoch this machine has accepted
  revokedFingerprints: string[]  // fingerprints explicitly revoked (see §6.5)
  updatedUtc:    string          // ISO-8601, audit only
```

- Serialized via a new source-gen `TrustStateJsonContext` (AOT-safe, mirrors `LayoutJsonContext`). Reads tolerate a missing file (epoch 0, empty revoked list = first run).

### 6.3 Anti-downgrade rule

On both verify gates (§5.2 step 4, §5.3 step 3):

1. Load `TrustState` (missing → epoch 0).
2. If `envelope.epoch < state.epoch` → **INT008** (reject; a replay of a pre-rotation release).
3. If any accepted signature's `fingerprint` ∈ `state.revokedFingerprints` → **INT001** (reject; explicitly revoked even if still in the baked set).
4. On a **successful update apply**, advance the store: `state.epoch = max(state.epoch, envelope.epoch)` and merge any revocations the (now-trusted) update declared (§6.5). Advancing only after a *verified* apply prevents an attacker priming the epoch with a forged high value.

The epoch is authored by the publisher and bumped **only** when a key is retired/revoked (not per release), so honest older bundles within the same epoch still install. It lives in the signed `files`? — No: `epoch` is a top-level envelope field. To prevent an attacker lowering it, the epoch **must be part of the signed bytes**. Therefore extend `ComputeSignedBytes` in v2 to sign `SHA-256(UTF-8(JSON(files) || epoch))` — *this is the one deliberate change to the signed message*, gated on `version >= 2` so v1 bundles keep the old `files`-only computation. (v1 bundles are treated as epoch 0.)

### 6.4 Config seam — `TrustPolicy`

A single struct threaded through `PipelineContext` (replacing the dead `ExpectedPublisherKeyFingerprint`, `PipelineContext.cs:106`) and passed to both gates:

```
TrustPolicy
  trustedFingerprints: FrozenSet<string>   // the baked set (§3.2)
  requireSigned:       bool                // true on update path, false on fresh install
  trustStorePath:      string              // §6.2 (overridable for tests)
```

Bootstrapper (`Program.cs`) and the update path (`UpdateService` / `UpdateDownloader` / `DefaultUpdateLauncher`) construct it with `requireSigned` set appropriately (§3.4). Tests inject a fake set + temp store path.

### 6.5 Revocation delivery (seam only — Stage 2 builds the data model, OTA deferred)

The **data model** (a `revokedFingerprints` list in both the signed envelope — a new optional `revoked[]` field — and the persisted store) is built now. **Delivery**: a signed update whose envelope declares `revoked: [ "<oldFingerprint>" ]` and bumps `epoch`, once verified and applied, merges those fingerprints into the store; thereafter any bundle signed *only* by a revoked key is rejected (§6.3 step 3) even though its fingerprint is still in older engines' baked sets. This is the "revoked key can't be resurrected by a downgrade/replay" guarantee. The **general OTA trusted-set update** (adding *new* keys to trust without an engine rebuild) reuses this exact channel but is deferred (§10) — Stage 2 ships the revoke half (remove trust) because it is the security-critical direction; add-trust can wait for a rebuilt engine.

---

## 7. Rotation runbook (dual-sign overlap)

**Invariant: trust leads, signing follows.** A new key must be *trusted* by the fleet before any release is signed *only* with it; an old key must stop being *used to sign* before it is *removed from trust*. Violating either strands clients.

### 7.1 Steady state

Releases are signed with the current key `K_old`. Every shipped engine's baked set contains `fingerprint(K_old)`.

### 7.2 Rotation procedure

1. **Generate `K_new`** (§8.1) in the vault. Do not sign anything with it yet.
2. **Add trust (lead).** Ship an engine build whose baked set = `{ K_old, K_new }`. Releases in this window are still **dual-signed**? Not yet — first just widen trust. Bundles are still signed with `K_old` only. Wait until this engine is the installed baseline across the fleet (the "generous window" — e.g. one full release cycle / N weeks, per your update-adoption telemetry).
3. **Dual-sign overlap.** Once `{K_old, K_new}` trust is widespread, sign every release with **both** `K_old` and `K_new` (the v2 `signatures[]` carries two entries over the same `files`). Now:
   - clients still on the old engine (trust `{K_old}`) accept via the `K_old` signature;
   - clients on the new engine (trust `{K_old, K_new}`) accept via either.
   Keep dual-signing for a **generous** window (long enough that essentially all clients have updated past step 2's engine).
4. **Stop signing with `K_old` (signing follows).** Sign releases with `K_new` only. Bump the **epoch** and declare `revoked: [fingerprint(K_old)]` in the envelope **only if** `K_old` is being retired for compromise; for a clean scheduled rotation, bump epoch without revoke (retire-not-revoke). Ship an engine whose baked set = `{ K_new }` (optionally still listing `K_old` for a final grace period). Clients that verified the epoch bump will now refuse older `K_old`-only bundles (anti-downgrade).
5. **Remove `K_old` from trust.** Once no supported release is signed by `K_old`, drop it from the baked set entirely in the next engine build.

### 7.3 Emergency revocation (compromise)

If `K_old` leaks: immediately (a) start signing with `K_new` only, (b) bump epoch **and** set `revoked: [fingerprint(K_old)]`, (c) ship an engine with baked set `{K_new}`. Clients that take *any* verified update signed by `K_new` record the revocation + epoch and thereafter reject every `K_old`-signed bundle, blocking replay of the leaked-key releases. Clients that never update remain exposed to their already-installed baseline — an inherent limit of client-side revocation without OTA (see §10).

### 7.4 Signer API for dual-sign

`IntegrityBuilder` (`IntegrityBuilder.cs`) gains `SigningKeys(params string[] pemPaths)` / repeatable `.AddSigningKey(path)` (superset of today's single `SigningKey`, `IntegrityBuilder.cs:14`), plus `.Epoch(int)` and `.Revoke(params string[] fingerprints)`. `IntegrityConfiguration` (`IntegrityConfiguration.cs`) grows `IReadOnlyList<string> SigningKeyPaths`, `int Epoch`, `IReadOnlyList<string> RevokedFingerprints`. `EcdsaManifestSigner.Sign` loops the keys producing one `SignatureEntry` each.

---

## 8. Key lifecycle

### 8.1 Generate

ECDSA P-256 private key, PEM (the signer already loads PEM: `EcdsaManifestSigner.cs:71-73` `ImportFromPem`):

```
# any of:
openssl ecparam -name prime256v1 -genkey -noout -out falkforge-signing.pem
# or .NET:
#   using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
#   File.WriteAllText("falkforge-signing.pem", k.ExportECPrivateKeyPem());
```

Derive the fingerprint to bake into engines:

```
#   SHA-256 of ExportSubjectPublicKeyInfo(), uppercase hex, no separators
```

### 8.2 Store

Private key: **vault / CI secret store only** (never in the repo, never in a bundle) — `rules/security.md` "No secrets in code/config/env files." Two active credentials for zero-downtime rotation (§7 dual-sign is exactly this). The `IntegrityConfiguration` vault fields already exist (`IntegrityConfiguration.cs:8-9` `VaultProvider`/`VaultKeyRef`) as the eventual home; Stage 1 accepts a CI-provided PEM path (`SigningKeyPath`) sourced from the secret store at build time.

### 8.3 What the private key signs, going forward

- **Every bundle** built with `.Integrity()` (full and delta): the `files` list (+ epoch, v2) → `signatures[]` in the manifest envelope.
- **Every update / delta bundle** on the feed — updates are just bundles, and the require-signed policy (§3.4) makes their signature mandatory.
- It does **not** sign the PE stub (that is Authenticode's separate, optional job) and does not sign the TOC directly (the TOC is bound transitively via the signed `files` hashes + `SignedPayloadTocVerifier`).

### 8.4 Loss / leak consequences

- **Lost (no leak):** cannot sign new releases. Recover by rotating to `K_new` (§7.2) — no client impact if done before the old key's releases age out.
- **Leaked:** attacker can forge trusted signatures for as long as the leaked fingerprint stays trusted. Mitigation = emergency revocation (§7.3): rotate + epoch-bump + revoke, pushed via the next update. Residual exposure = clients that never update (bounded by your update-adoption). This is why the epoch/revocation store (§6) is Stage 2, not "someday."

---

## 9. Implementation surface

Legend: **N** new file, **M** modified. Every `.cs` edit is followed by a full-solution build (Gate 2); tests land in the same green commit as their implementation (Gate 1).

### Stage 1 — multi-sig manifest + trusted-set pin + verify-any + build-time injection

Protocol / codec:
- **M** `src/FalkForge.Engine.Protocol/Integrity/ManifestSignatureEnvelope.cs` — add `signatures[]`, `epoch`; keep v1 `publicKey`/`signature` as optional (read-compat).
- **N** `src/FalkForge.Engine.Protocol/Integrity/SignatureEntry.cs` — new wire type (§4.2).
- **M** `src/FalkForge.Engine.Protocol/Integrity/IntegrityEnvelopeJsonContext.cs` — register `SignatureEntry` + list.
- **M** `src/FalkForge.Engine.Protocol/Integrity/IntegrityEnvelopeCodec.cs` — v1→v2 parse adapter (§4.4); add `VerifyTrusted(envelope, trustedSet)` (§5.1); keep `ComputeSignedBytes` (v1) + add epoch-inclusive v2 variant (§6.3).
- **M** `src/FalkForge.Engine.Protocol/Integrity/SignedPayloadTocVerifier.cs` — call `VerifyTrusted` instead of `VerifySignature`; accept a trusted set + policy (§5.3).

Engine:
- **M** `src/FalkForge.Engine/Integrity/PayloadIntegrityGate.cs` — replace dead `expectedPublisherKeyFingerprint` with `TrustPolicy`; verify-any + presence policy (§5.2). Remove `VerifyPublisherPin`/`NormalizeFingerprint` (INT005 retired).
- **N** `src/FalkForge.Engine/Integrity/TrustPolicy.cs` — the §6.4 struct.
- **N** `src/FalkForge.Engine/Integrity/BakedTrustedKeys` — generated constant (see MSBuild below).
- **M** `src/FalkForge.Engine/Pipeline/PipelineContext.cs` — replace `ExpectedPublisherKeyFingerprint` with `TrustPolicy` (`PipelineContext.cs:106`).
- **M** `src/FalkForge.Engine/Pipeline/ApplyStep.cs` — pass `TrustPolicy` (`ApplyStep.cs:66-67`).
- **M** `src/FalkForge.Engine/Program.cs` — construct fresh-install `TrustPolicy` (requireSigned=false) and pass to both `SignedPayloadTocVerifier.Verify` calls (`Program.cs:341`, `442`).
- **M** `src/FalkForge.Engine/FalkForge.Engine.csproj` + a `.targets` — MSBuild `FalkForgeTrustedKey` items → generated `TrustedKeys.g.cs` (§3.2), reusing the `RoslynCodeTaskFactory` idiom from `Sdk.targets:43`.

Build-time signer / fluent API:
- **M** `src/FalkForge.Compiler.Bundle/Compilation/EcdsaManifestSigner.cs` — multi-key sign → `signatures[]` (§4.5); load N PEM keys.
- **M** `src/FalkForge.Compiler.Bundle/Compilation/BundleIntegritySigner.cs` — pass epoch/keys through (structure unchanged, `BundleIntegritySigner.cs:41`).
- **M** `src/FalkForge.Core/Models/IntegrityConfiguration.cs` — `SigningKeyPaths`, `Epoch`, `RevokedFingerprints` (§7.4).
- **M** `src/FalkForge.Core/Builders/IntegrityBuilder.cs` — `AddSigningKey`/`SigningKeys`, `Epoch`, `Revoke` (§7.4).

Stage 1 new tests:
- **N** `EcdsaManifestSignerTests` cases: dual-key sign produces two entries over one `files`.
- **N** `IntegrityEnvelopeCodecTests`: `VerifyTrusted` accept-any (either of two trusted keys), reject untrusted key, reject lying fingerprint, empty-set = consistency-only.
- **N** **Re-sign attack test** (B1): tamper payloads, re-sign with a fresh key, assert `VerifyTrusted` → INT001 when that key isn't in the baked set.
- **N** v1→v2 back-compat: a stored v1 envelope verifies iff its single key is in the trusted set.
- **N** MSBuild injection test: build the engine with a sample `FalkForgeTrustedKey`, assert the generated constant contains it.

### Stage 2 — epoch/revocation store + require-signed update path + coverage extension + bypass paths

Epoch / revocation:
- **N** `src/FalkForge.Engine/Integrity/TrustState.cs` + `TrustStateStore.cs` + `TrustStateJsonContext.cs` — the §6.2 persisted store (load/advance, AOT-safe).
- **M** `IntegrityEnvelopeCodec` — sign/verify epoch as part of v2 signed bytes (§6.3); parse `revoked[]`.
- **M** both gates — epoch anti-downgrade (INT008) + revoked-fingerprint rejection (§6.3).

Require-signed on updates:
- **M** `src/FalkForge.Engine/Pipeline/UpdateService.cs` — build a require-signed `TrustPolicy`; verify the downloaded bundle's manifest **before** launch.
- **M** `src/FalkForge.Engine/Download/UpdateDownloader.cs` — after download, run the trust gate on the staged bundle; discard + warn on failure (do not launch). Today it launches on SHA-only (`UpdateDownloader.cs:126-137`).
- **M** `src/FalkForge.Engine/UpdateLauncher.cs` — the launcher stays the last (Authenticode) gate; the *integrity* gate runs before it in the service. (No structural change required if the service verifies first, but assert order.)

Coverage extension (§5.4):
- **M** `EcdsaManifestSigner` / `BundleIntegritySigner` — include UI/engine executed payloads in `files`.
- **M** both gates — reject an unmatched *executable* TOC entry instead of skipping (`SignedPayloadTocVerifier.cs:39-42`, `79-84`).

Bypass paths:
- **M** `src/FalkForge.Cli/Commands/ExtractCommand.cs` — call `SignedPayloadTocVerifier.Verify` (consistency-only `TrustPolicy`, or `--require-signed` flag) before extracting (`ExtractCommand.cs:72-153`).
- **M** `src/FalkForge.Decompiler/MigrationProjectGenerator.cs` — same gate before `BundleReader.Extract`/`ExtractPayload` (`MigrationProjectGenerator.cs:257`, `271`).

Stage 2 new tests:
- **N** **Strip-downgrade attack test** (B2): strip `ManifestSignature` to null on an update; assert require-signed path → INT007 (rejected, not launched).
- **N** Epoch anti-downgrade: bundle epoch < stored epoch → INT008.
- **N** Revocation: after applying an update that revokes `K_old`, a `K_old`-signed bundle → INT001.
- **N** Store persistence: epoch advances only after a verified apply; forged high epoch on an unverified bundle does not prime the store.
- **N** Coverage: tampered UI payload (now in signed set) → INT006/INT004.
- **N** `forge extract` / `forge migrate` reject a tampered signed bundle.

### Existing tests to update for the format change

- `tests/FalkForge.Engine.Protocol.Tests/Integrity/SignedPayloadTocVerifierTests.cs` — signature now needs a trusted set; add set + policy args.
- `tests/FalkForge.Engine.Tests/Integrity/PayloadIntegrityGateTests.cs` — drop `expectedPublisherKeyFingerprint`/INT005 cases, add `TrustPolicy`/verify-any/INT007.
- `tests/FalkForge.Engine.Tests/Integrity/IntegritySignatureContextRegressionTests.cs` — v2 JSON context.
- `tests/FalkForge.Compiler.Bundle.Tests/Compilation/EcdsaManifestSignerTests.cs`, `BundleCompilerSigningTests.cs`, `BundleIntegrityTests.cs` — envelope now has `signatures[]`.
- `tests/FalkForge.Integration.Tests/BundlePayloadTrustBindingTests.cs`, `BundleSigningEndToEndTests.cs` — end-to-end with a baked trusted set.
- `tests/FalkForge.Compiler.Bundle.Tests/Compilation/ManifestFieldPreservationTests.cs` — envelope shape.
- `tests/FalkForge.Cli.Tests/BundleRegionHintTests.cs` — if it asserts envelope JSON.

Docs to update: `docs/provenance.md §3` (replace host-pin seam with baked set; retire INT005; add INT007/INT008) and the manual (assessment task C14 manual section, #19).

---

## 10. Non-goals / deferred

- **OTA trusted-set *addition* (add-trust without an engine rebuild).** The seam is built in §6.5 (signed update carries trust changes; store records them), and the security-critical **revoke** half ships in Stage 2. Delivering *new* trusted keys OTA (so a plain download becomes trust-updatable without a rebuilt engine) is deferred to a follow-up — build-time pin injection (§3.2) is sufficient for the first release, because a fresh install is self-consistent and rotation is handled by dual-sign + engine reship (§7).
- **Authenticode / paid code-signing certificate.** Separate, optional layer. It protects only the PE stub (cannot cover overlay payloads) and already has an update-launcher seam (`DefaultUpdateLauncher` + `UpdatePublisherThumbprint`). Not required by this design and not a substitute for it.
- **Require-signed everywhere (fresh install too).** The `TrustPolicy.requireSigned` seam exists (§6.4); flipping it on for fresh installs is a future host-policy decision, not this milestone. Fresh install of an unsigned bundle stays allowed-but-warned (§3.4).
- **Vault-native signing at build time.** `IntegrityConfiguration` vault fields exist but Stage 1 sources the PEM from a CI secret; wiring a live vault provider is out of scope here.
