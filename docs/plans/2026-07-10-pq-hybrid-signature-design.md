# Hybrid Post-Quantum Manifest Signatures (ML-DSA / FIPS 204) — Design + Feasibility

Date: 2026-07-10. Repo: D:\Git\FalkInstaller. Author: design agent (read-only task; no product code touched).

---

## 0. FEASIBILITY VERDICT (the gate) — **GO, with one deployment caveat**

**.NET 10 ships a usable, stable, NativeAOT-safe ML-DSA API, and it works at runtime on this machine — verified empirically with a throwaway probe (CoreCLR *and* a NativeAOT-published exe).**

Probe: `scratchpad\mldsa-probe\` (console app, outside the repo, not in the solution). Results on this box (Windows 11 build 26200, .NET runtime 10.0.9, SDK 10.0.301; repo `global.json` pins 10.0.103 + `rollForward: latestFeature`, so the repo builds with the same 10.0.3xx band):

| Fact | Result |
|---|---|
| `System.Security.Cryptography.MLDsa.IsSupported` | **True** (Windows CNG-backed) |
| Experimental gate | **None.** Compiles with zero warnings and **no** `SYSLIB5006` opt-in — the ML-DSA API graduated to stable in .NET 10 GA (verified by compiling a pragma-free reference under default settings; `TreatWarningsAsErrors` needs nothing) |
| Generate ML-DSA-65 keypair | Works (`MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65)`) |
| Sign + verify | Works (`SignData` / `VerifyData`); tampered signature correctly rejected |
| SPKI export/import round-trip | Works (`ExportSubjectPublicKeyInfo` 1974 B → `MLDsa.ImportSubjectPublicKeyInfo` → verifies) |
| PEM round-trips | Works both ways: `ExportSubjectPublicKeyInfoPem` (`BEGIN PUBLIC KEY`), `ExportPkcs8PrivateKeyPem` (`BEGIN PRIVATE KEY`, seed form, 127 chars) → `MLDsa.ImportFromPem` → signs, verifies |
| Context-string domain separation | Works (`SignData(data, context)`; verify with wrong/absent context correctly fails) |
| **NativeAOT publish** | **Works.** `PublishAot=true` + `InvariantGlobalization=true`, win-x64: 1.47 MB exe, all of the above pass identically. No reflection, no trimming warnings. (The only publish hiccup was this sandbox shell missing `vswhere.exe` on PATH — an environment issue, not an ML-DSA one.) |

Measured sizes (exactly matching FIPS 204):

| Algorithm | Raw pubkey | SPKI | Signature |
|---|---|---|---|
| ML-DSA-44 | 1312 B | 1334 B | 2420 B |
| **ML-DSA-65** | **1952 B** | **1974 B** | **3309 B** |
| ML-DSA-87 | 2592 B | 2614 B | 4627 B |

Exact API surface used (all in-box `System.Security.Cryptography`, no package):
`MLDsa.IsSupported`, `MLDsa.GenerateKey(MLDsaAlgorithm)`, `MLDsaAlgorithm.MLDsa44/65/87` (`.Name` = `"ML-DSA-65"` etc.), `mldsa.SignData(byte[] data[, byte[] context])`, `mldsa.VerifyData(data, sig[, context])`, `ExportSubjectPublicKeyInfo()`, `ExportMLDsaPublicKey()`, `ExportPkcs8PrivateKey()/Pem()`, `ExportSubjectPublicKeyInfoPem()`, `MLDsa.ImportSubjectPublicKeyInfo(byte[])`, `MLDsa.ImportFromPem(string)`.

**The one honest caveat — verifier-side OS support.** On Windows, .NET delegates ML-DSA to CNG. `IsSupported == true` here (a current Windows 11 build), but the *engine runs on customer machines*, and older Windows (Windows 10, early Windows 11 builds without the PQC CNG additions) will report `IsSupported == false` — there the BCL cannot verify an ML-DSA signature at all. This does **not** block the design (see §2.4: OS incapability is not attacker-controllable, so classical-fallback-when-platform-incapable is sound), but it must be an explicit, logged policy decision, and it means PQ enforcement is only as universal as the customer OS floor. This is the single biggest open decision to put in front of the human (§8).

BouncyCastle fallback: **not needed** and not recommended. The BCL primitive works, is in-box (zero new dependency — matches the repo's no-deps/AOT ethos), FIPS-aligned, and AOT-clean. BC would only re-enter the picture if the human decides old-Windows verifiers must verify PQ too (§8, option C) — at the cost of a large managed dependency inside a NativeAOT security boundary.

---

## 1. Current trust surface (what the design plugs into)

Read in full; file references are load-bearing for the seam:

- **Envelope + codec** — `src/FalkForge.Engine.Protocol/Integrity/ManifestSignatureEnvelope.cs`, `SignatureEntry.cs`, `IntegrityEnvelopeCodec.cs`. v2 envelope = `Files` + `Signatures[]` (+ optional signed `Epoch`/`Revoked`). Each `SignatureEntry` = `{keyId, fingerprint, publicKey(SPKI b64), signature(b64)}` — **no per-entry algorithm field today**. The envelope-level `Algorithm` string (`IntegrityEnvelopeCodec.AlgorithmId = "ECDSA-P256"`) is written but never dispatched on at verify time; verification hard-assumes P-256: `EcdsaLowS.IsCanonical` rejects any non-64-byte signature, then `ECDsa.VerifyHash`. Signed message = `ComputeSignedBytes(files, epoch, revoked)`; signers sign `SHA-256(message)`.
- **Verify paths** — `MatchTrustedSignature` (verify-any, first trusted valid wins; per-entry: lying-fingerprint check → revocation skip → trust-set check → low-S check → crypto verify) and `CollectTrustedSignatures` (C19 quorum evidence; distinct-by-fingerprint) in the codec; routed via `SignedPayloadTocVerifier` / `BundleTrustVerifier.cs` and the Engine gates (`BundleTrustGate`, `PayloadIntegrityGate`, `StagedUpdateVerifier`).
- **Trust anchor** — `src/FalkForge.Engine/Integrity/EngineTrustAnchor.cs`: freeze-once union of MSBuild-baked fingerprints (`TrustedKeys.targets` → generated `BakedTrustedKeys.Fingerprints` + `.Roles`) and code-registered keys (`TrustPublicKey/Pem/Fingerprint`). Fingerprint = SHA-256 of SPKI, uppercase hex, 64 chars (`ComputeFingerprint`). `EngineTrustAnchor.Normalize` hard-codes 64 hex chars — fine, ML-DSA fingerprints are also SHA-256/64-hex.
- **Policy/quorum** — `TrustRole.cs` (flags), `BakedTrustPolicy.cs` (per-`OperationKind` `PolicyRule`), `QuorumEvaluator.cs` (bipartite distinct-key matching), threaded via `src/FalkForge.Engine/Integrity/TrustPolicy.cs`.
- **Signing side** — `src/FalkForge.Core/Signing/ISignatureProvider.cs` (contract: sign `SHA-256(message)`, return **P1363** in `ProviderSignature`), `PemSignatureProvider.cs`, `EphemeralSignatureProvider`, `EcdsaSignatureHelper.cs` (low-S canonicalize); assembled by `src/FalkForge.Compiler.Bundle/Compilation/EcdsaManifestSigner.cs` (chokepoint that builds `SignatureEntry` rows; defense-in-depth low-S there too). Remote: `src/FalkForge.Signing.SignServer/SignServerSignatureProvider.cs` (DER→P1363 normalization at the boundary).
- **Persisted anti-downgrade state** — `TrustState`/`TrustStateStore` (epoch + revoked fingerprints, ACL'd store, advanced via elevated companion).

Two structural facts the design exploits:

1. **Unknown JSON fields are ignored** by the source-generated deserializer, and **any non-64-byte signature is skipped** by shipped verifiers (fails the low-S length gate and iteration continues). So an already-shipped engine that meets a hybrid envelope skips the ML-DSA entries and verifies the ECDSA entry exactly as today → additive wire change, no version bump required.
2. **Trust decisions are never read from the bundle** (C14 invariant). Therefore the *only* sound place for "this signer must have a PQ signature" is the pinned trust record — which is exactly how the anti-downgrade problem (§2.2) is solved.

---

## 2. The hybrid scheme

### 2.1 Algorithm choice: **ML-DSA-65** (default), parameter-set-agnostic plumbing

- **ML-DSA-65** (FIPS 204 Category 3, ~AES-192): the industry consensus default for code/artifact signing. Measured: pubkey 1952 B (SPKI 1974 B), signature 3309 B.
- Why not **ML-DSA-44** (Cat 2): the size saving (~900 B/sig) is irrelevant next to bundle payloads, and Cat 2 gives thinner margin for a trust anchor that must survive decades of cryptanalysis — the entire motivation here is longevity.
- Why not **ML-DSA-87** (Cat 5, CNSA 2.0's pick): +1.3 KB/sig buys margin most publishers don't need; however, the `algorithm` field (§2.3) carries the parameter set name, so a publisher who must meet CNSA 2.0 can use 87 without any wire change. Verifier accepts all three sets it can name; **65 is the shipped default**.
- **Size impact**: one hybrid signer adds one ML-DSA entry ≈ base64(1974) + base64(3309) + JSON ≈ **~7.2 KB of envelope JSON** (vs ~0.4 KB for an ECDSA entry). Against multi-MB bundles: noise. Even a 3-signer hybrid quorum is ~22 KB. Not a factor.
- **Signing mode**: *pure* ML-DSA (`SignData`) over the **same canonical message bytes** `ComputeSignedBytes(files, epoch, revoked)` that ECDSA hashes. ECDSA keeps its exact contract (sign `SHA-256(message)`, P1363, low-S); ML-DSA signs the message directly per FIPS 204 (it has no meaningful "sign this external SHA-256 hash" mode — HashML-DSA/pre-hash exists but buys nothing here and adds a second variant). The *signed content* is identical for both; only the algorithm-native step differs. Recommend the **context string `"falkforge/manifest"`** (probe-verified): free FIPS-204 domain separation ensuring a manifest ML-DSA signature can never be replayed as any other FalkForge ML-DSA artifact signature. Decide once, before first ship — it is part of the wire contract forever.

### 2.2 Hybrid model + anti-downgrade (the crux)

**Model: hybrid = classical AND pq, expressed as a *bound key pair in the pinned trust record*, not as two independent trusted keys.**

A hybrid signer owns two private keys: ECDSA-P256 (existing) and ML-DSA-65. At build time, both sign the same canonical bytes and contribute **two `SignatureEntry` rows** (classical first — keeps first-wins verify hitting the cheap path). At verify time:

- The classical entry is matched exactly as today (fingerprint honesty → revocation → trust set → low-S → verify).
- **New companion rule**: after the classical entry cryptographically verifies, the verifier consults the pinned **PQ companion map** `classicalFp → pqFp` (from the trust anchor, §2.3). If the accepted classical fingerprint **has a registered companion**, the envelope MUST also contain an ML-DSA entry whose fingerprint equals that companion fingerprint, whose fingerprint re-derives from its own SPKI (same lying-fingerprint defense), and whose signature verifies over the same canonical bytes. If any of that fails → the classical entry **does not count** (skip / fail with new code **INT011**: "trusted key requires a post-quantum companion signature that is missing or invalid").
- A trusted classical fingerprint **without** a registered companion verifies exactly as today (backward compatible; pre-PQ engines-and-bundles unaffected).

**Why this kills the downgrade/strip attack.** The "expect PQ" bit lives in the engine binary (baked set / code registration) — the channel C14 already assumes the attacker cannot rewrite (same trust level as `BakedTrustedKeys.Fingerprints` itself). An attacker who strips the ML-DSA entry from a hybrid envelope leaves a classical entry whose companion requirement cannot be met → INT011, rejected. An attacker cannot "un-declare" the companion because nothing in the bundle declares it. This is deliberately the same shape as C14's answer to self-describing keys: **the bundle proves, the binary decides.**

Threat-model honesty, enumerated:
- *Quantum forger of ECDSA-P256, hybrid-pinned engine*: forged classical sig verifies, but attacker cannot produce the companion ML-DSA signature → rejected. ✔ (This is the scenario the feature exists for.)
- *Strip PQ entry*: INT011. ✔
- *Strip both entries / re-sign with own keys*: untrusted fingerprint, exactly today's INT001. ✔
- *Replay a genuine pre-hybrid classical-only release*: it is a legitimately-signed old artifact; on the require-signed update path the existing epoch (INT008) / downgrade policy governs it — orthogonal to PQ, unchanged. Publishers doing the hybrid cutover SHOULD bump the key epoch so the cutover is also replay-protected (§6).
- *Weakest-link caveat (must be documented loudly)*: if an engine pins hybrid key H **and** non-companioned classical key L, a quantum forger targets L. **PQ protection is only as strong as the weakest pinned key.** The `TrustedKeys.targets` generator should emit a build **warning** when a baked set mixes companioned and un-companioned keys, and `EngineTrustAnchor` should surface the same via `ConfigurationWarnings`.
- *"PQ optional / preferred" mode*: rejected as a verification mode. Verify-if-present-else-accept provides **zero** anti-strip value (the attacker just strips). Optionality exists only *per key* — a key either has a pinned companion (PQ enforced) or it doesn't (classical-only) — never as a soft global toggle. During migration, bundles may carry PQ signatures that engines don't yet require (§6); that's harmless surplus, not a mode.

**Quorum interaction: a hybrid pair is ONE signer.** The ML-DSA companion entry is a *validity condition* on the classical entry, not an independent quorum member. `CollectTrustedSignatures` keeps collecting **classical identities only** (a companion-satisfied classical fp contributes one `TrustedSignature`, roles resolved from the classical fp as today); ML-DSA entries never enter the collected set themselves. This preserves the C19 distinct-key guarantee — one person holding one hybrid pair can never fill two slots of a 2-distinct rule — with **zero changes** to `QuorumEvaluator`, `TrustRole`, `BakedTrustPolicy`, or `PolicyRule`.

### 2.3 Trusted-key handling

- **One combined trust record per signer** (decision: combined, not separate). A separate free-standing PQ trusted key would (a) let a PQ fp accidentally satisfy classical-shaped rules, (b) break the one-signer-one-vote quorum property, and (c) not express "expect PQ" at all. The record is: classical fingerprint (primary identity, carries the roles) + optional PQ companion fingerprint.
- **Fingerprint derivation**: unchanged — `IntegrityEnvelopeCodec.ComputeFingerprint` = SHA-256 of the SPKI, uppercase hex, 64 chars. The ML-DSA SPKI (1974 B for -65) hashes to the same 64-hex shape, so `EngineTrustAnchor.Normalize`, the frozen-set comparers, display formatting, and `TrustedKeys.targets` hex normalization all work as-is. The SPKI's embedded AlgorithmIdentifier OID makes cross-algorithm fingerprint collision a non-issue.
- **Baked plumbing** (`src/FalkForge.Engine/TrustedKeys.targets`): add optional item metadata, mirroring the existing `Roles=` idiom:
  ```xml
  <FalkForgeTrustedKey Include="A1B2...=64hex classical fp"
                       Roles="release"
                       PqFingerprint="C3D4...=64hex ML-DSA SPKI fp"
                       PqAlgorithm="ML-DSA-65" />
  ```
  The RoslynCodeTaskFactory generator additionally emits:
  ```csharp
  internal static readonly FrozenDictionary<string, string> PqCompanions;   // classicalFp -> pqFp
  internal static readonly FrozenDictionary<string, string> PqAlgorithms;   // pqFp -> "ML-DSA-65"
  ```
  (PQ companion fingerprints are **not** added to `Fingerprints` — they are not independent trust anchors.) Property short-form `-p:FalkForgeTrustedKey=<fp>` stays classical-only; hybrid pinning uses the item form (or code registration).
- **Code registration** (`EngineTrustAnchor`): new pre-freeze overloads —
  ```csharp
  TrustHybridKey(ReadOnlySpan<byte> classicalSpki, ReadOnlySpan<byte> pqSpki, TrustRole roles = TrustRole.Release);
  TrustHybridFingerprint(string classicalFp, string pqFp, string pqAlgorithm = "ML-DSA-65", TrustRole roles = TrustRole.Release);
  ```
  plus a frozen `EffectivePqCompanions` published atomically with `EffectiveFingerprints`/`EffectiveRoles` in `Freeze()`. Additive-union rule: registering a companion for an already-baked classical fp *tightens* trust (adds a requirement) — allowed; conflicting companion registrations for the same classical fp throw (fail loud, no silent last-wins on a security anchor).
- **Threading**: `TrustPolicy` (Engine) gains `IReadOnlyDictionary<string,string>? PqCompanions` (+ pq algorithms map), passed through `BundleTrustVerifier.VerifyBundleContent` / `SignedPayloadTocVerifier.Verify` → codec, exactly as `roles`/`policyTable` were threaded for C19. Null/empty = today's behavior, bit-for-bit.

### 2.4 Envelope / codec changes

- **`SignatureEntry` + optional `algorithm` field** (`[JsonPropertyName("algorithm")]`, `JsonIgnore WhenWritingNull/Default`): absent ⇒ `"ECDSA-P256"` (every existing envelope stays byte-identical and semantically unchanged); `"ML-DSA-65"` (/`-44`/`-87`) for PQ entries. Envelope stays **version 2** — the change is additive, old verifiers skip what they don't know (verified against the shipped code: unknown JSON fields ignored; 3309-byte sig fails the 64-byte low-S length gate and is skipped with iteration continuing). The envelope-level `Algorithm` string stays `"ECDSA-P256"` (it is informational today; redefining it would confuse old readers).
- **Signed bytes: unchanged and shared.** Both algorithms cover the identical `ComputeSignedBytes(files, epoch, revoked)` output. ECDSA path: `SHA-256(message)` → `VerifyHash`, low-S rule untouched. ML-DSA path: `VerifyData(message, sig, context)` (needs the message bytes, which the verifier already has in hand before hashing — keep both around, trivial).
- **Verifier dispatch** in `MatchTrustedSignature` / `CollectTrustedSignatures`:
  1. Partition entries by `algorithm`: classical entries iterate exactly as today; ML-DSA entries are indexed by fingerprint into a side map (after the same base64/lying-fingerprint hygiene) and are *never* independently matched against the trust set.
  2. After a classical entry passes step (c) (crypto verify), apply the companion rule (§2.2) using the side map + pinned `pqCompanions`. Companion checks: fingerprint equality with the pinned companion, SPKI-fingerprint honesty, algorithm-name match with the pinned algorithm, `MLDsa.ImportSubjectPublicKeyInfo` + `VerifyData` over the message. On failure: record `sawMissingPqCompanion` and continue iterating (another trusted signature may still satisfy — same continue-shape as the revoked-key skip); if nothing matches and that flag is set, fail with **INT011** (fail-loud specific message) rather than generic INT001.
  3. Entries with an *unknown* algorithm string are skipped (forward-compat for a future ML-DSA-87-only or SLH-DSA entry an older-but-PQ-aware engine doesn't know).
  4. **Platform incapability policy**: if a companion is required but `MLDsa.IsSupported == false` on the verifying machine, **accept on classical + log a prominent warning** (recommended, see §8 for the alternative). Rationale: the victim's OS version is not attacker-controllable — an attacker cannot *cause* `IsSupported` to be false — so this is graceful degradation, not a downgrade vector. The realistic quantum-era assumption is that OS floors will have long since caught up. This MUST be a named, logged, documented decision, not an accident.
- **Low-S / ECDSA path**: entirely untouched — it now simply runs only for entries whose (defaulted) algorithm is `ECDSA-P256`, which is exactly the set it runs on today.

### 2.5 Provider changes (signing side)

- **`ProviderSignature`** (Core/Signing) gains `public string Algorithm { get; init; } = "ECDSA-P256";` — default preserves every existing provider (including third-party `ISignatureProvider` impls) verbatim.
- **`ISignatureProvider` contract note** amended: an ECDSA provider signs `SHA-256(message)`/P1363 (unchanged); an ML-DSA provider signs the raw message bytes (pure ML-DSA, fixed context) and sets `Algorithm`. Same interface — the contract is per-algorithm, dispatched by the field. No second interface needed.
- **New `MLDsaPemSignatureProvider`** (Core/Signing, sibling of `PemSignatureProvider`): loads an ML-DSA private key PEM (PKCS#8 seed form — probe-verified round-trip via `ExportPkcs8PrivateKeyPem`/`ImportFromPem`), `SignData(message, Context)`, returns `ProviderSignature{Algorithm="ML-DSA-65"}`. Import-per-call + dispose, like the ECDSA twin. Fails loud (`SGN011`-ish) when `MLDsa.IsSupported == false` on the **build** machine — signing has no fallback story and shouldn't (build machines are controlled; require a capable OS/.NET).
- **`EcdsaManifestSigner`** (rename candidate: `ManifestSigner`, but keep the file surgical in stage 1): at the envelope-assembly chokepoint, dispatch on `ProviderSignature.Algorithm` — low-S canonicalization **only** for classical entries; ML-DSA entries emitted as-is with the `algorithm` field set; classical entries ordered first. Key-pair config: `IntegrityConfiguration` gains `PqSigningKeyPath(s)` (paired positionally with the classical `SigningKeyPaths`) or, cleaner, a `HybridKey(classicalPem, pqPem)` fluent unit on `BundleBuilder.Integrity()` — builder-level API is a stage-3 concern; stage 1 can take bare provider lists.
- **SignServer / HSM**: `SignServerSignatureProvider` stays ECDSA-only in stage 1. Keyfactor SignServer's ML-DSA worker support must be **assessed, not assumed** (PQC support in commercial signing services was still rolling out as of my knowledge horizon; the CE test-container path used by the e2e suite may lag further). The provider seam already fits (send raw bytes, get signature; ML-DSA has no DER/P1363 duality — the raw FIPS-204 signature bytes are the only encoding, so the format-converter step simply doesn't apply). Local-key PQ signing is the realistic first step; a hybrid signer can mix SignServer-ECDSA + local-PEM-ML-DSA in one build because providers are independent entries.

---

## 3. Backward + forward compatibility

| Scenario | Outcome |
|---|---|
| Old engine (shipped today) × hybrid bundle | Ignores `algorithm` JSON field; ML-DSA entry skipped (fails 64-byte low-S gate, iteration continues); ECDSA entry verifies as today. **Works.** (Add a regression test pinning this skip behavior.) |
| New engine, no companions pinned × any bundle | Codec paths take the null-companion branch — bit-for-bit today's behavior. |
| New engine, companion pinned × hybrid bundle | Classical AND PQ must verify. The feature. |
| New engine, companion pinned × old classical-only bundle from same signer | **INT011 — rejected by design.** Pinning the companion *is* the publisher's cutover statement ("no artifact of mine verifies classically alone anymore"). The rollout sequence (§6) exists precisely so this never bites a legitimate old artifact users still need. |
| New engine, companion pinned × old Windows (`MLDsa.IsSupported == false`) | Classical-only + loud log (recommended policy §2.4; human decision §8). |
| Envelope with unknown future algorithm | Entry skipped, other entries still evaluated (forward-compat). |
| v1 envelopes | Untouched — the v1→v2 adapter synthesizes a classical entry; no companion can be pinned against nothing; behavior unchanged. |

## 4. Migration / rollout story (signing leads, trust follows)

Mirrors the C18 rotation discipline, with the lead/follow order flipped because the *requirement* lives in the engine:

1. **Generate** the ML-DSA-65 keypair per signer identity; custody alongside the classical key it companions (they are one identity — compromise-recovery plans must treat the pair atomically).
2. **Dual-sign** all new bundles (classical + PQ entries). Old engines ignore PQ; new engines don't require it yet. Zero risk. Run here as long as old-engine/old-bundle coexistence is needed.
3. **Pin companions** in the next engine release (`PqFingerprint=` metadata / `TrustHybridKey`), ideally with a key-**epoch bump** in the same release so the pre-hybrid era is also replay-fenced on the update path (INT008 does the rest).
4. From then on: classical-only artifacts from that signer no longer verify on new engines. Publish that expectation in release notes.
5. **PQ key rotation** later = the existing rotation playbook (dual-sign old+new PQ, new engine pins new companion, epoch bump); a PQ-companion change is a `KeyChange` operation under `BakedTrustPolicy` exactly like a classical rotation.

## 5. What does NOT change

`QuorumEvaluator`, `TrustRole`, `PolicyRule`, `BakedTrustPolicy`, `TrustState`/`TrustStateStore` schema, epoch/revocation semantics (revocation of the *classical* fp retires the whole hybrid identity — the companion map is keyed by it), `EcdsaLowS`, v1 adapter, `ComputeSignedBytes`, all TOC/payload hashing, Authenticode paths, elevation protocol. The blast radius is deliberately: `SignatureEntry` (+1 optional field), codec verify internals, `ProviderSignature` (+1 defaulted field), 1 new provider, `TrustedKeys.targets` generator, `EngineTrustAnchor` (+2 overloads, +1 frozen map), `TrustPolicy` threading, `EcdsaManifestSigner` dispatch.

## 6. Staged implementation plan

**Stage 1 — the safe, reviewable first PR** (order within = TDD commits, each green):
1. `SignatureEntry.Algorithm` (optional, defaulted) + codec constant `MlDsa65AlgorithmId = "ML-DSA-65"`; regression test: today's envelopes serialize byte-identically; old-shape parse unchanged.
2. `ProviderSignature.Algorithm` (defaulted) + `MLDsaSignatureHelper` + `MLDsaPemSignatureProvider` (+ `MLDsa.IsSupported` fail-loud on sign). Context string decided and frozen here.
3. Codec sign path: `Sign`/`EcdsaManifestSigner` emit ML-DSA entries (classical-first ordering pinned by test).
4. Codec verify path: algorithm partition, companion map parameter, INT011, unknown-algorithm skip, platform-incapability branch (behind an injectable `isPqSupported` for testability).
5. Trust anchor: `TrustedKeys.targets` `PqFingerprint`/`PqAlgorithm` metadata → generated `PqCompanions`/`PqAlgorithms`; `EngineTrustAnchor.TrustHybrid*` + `EffectivePqCompanions` + mixed-set `ConfigurationWarnings`; conflicting-companion throw.
6. Thread through `TrustPolicy` → gates → `SignedPayloadTocVerifier`/`BundleTrustVerifier`/`StagedUpdateVerifier`.
7. Test battery (the security tests ARE the deliverable): hybrid sign→verify e2e; **strip-PQ ⇒ INT011**; classical-only + no companion ⇒ accepted; classical-only + companion ⇒ INT011; lying PQ fingerprint ⇒ rejected; wrong-context PQ sig ⇒ rejected; tampered message fails both; quorum counts hybrid pair as ONE signer (2-distinct rule NOT satisfied by one hybrid identity); old-verifier-shape simulation (algorithm-field-ignorant parse still verifies classical); `IsSupported=false` branch logs + accepts (or rejects, per §8 decision).
   - Engine/Protocol tests must gate PQ-runtime assertions on `MLDsa.IsSupported` so CI on older Windows images skips rather than fails (`Assert.Skip` / trait).
8. Docs: envelope wire-format section + threat-model paragraph (weakest-link caveat, §2.2).

**Stage 2**: quorum/e2e hardening — hybrid across rotation (dual-sign old+new PQ), epoch-bump-at-cutover recipe, revocation-of-hybrid-identity tests, `forge` CLI verify-path surfacing of INT011.
**Stage 3**: authoring ergonomics — `BundleBuilder.Integrity().HybridKey(...)`, `forge` key-gen helper for ML-DSA PEM, JSON config, demo 63 (`hybrid-pq-signing`), manual section.
**Stage 4**: remote signing — assess SignServer/Keyfactor ML-DSA worker availability; wire `SignServerSignatureProvider` PQ variant if real; else document local-PQ + remote-classical mixed pattern as the supported posture.

## 7. Size/perf notes

- Envelope growth ~7.2 KB per hybrid signer (§2.1) — negligible.
- ML-DSA-65 verify is fast (sub-millisecond scale on this class of hardware via CNG); one extra verify per accepted trusted signature, only when a companion is pinned. Keygen/sign also cheap (probe generated 3 keypairs + several sign/verify rounds with no observable delay). No hot-path concern; no Gate 6 tension (byte[] in/out, no per-iteration LINQ needed).
- NativeAOT probe exe was 1.47 MB with ML-DSA included — no measurable AOT size penalty concern for the Engine.

## 8. Risks & open decisions — RESOLVED (human decisions, 2026-07-10)

The open decisions below were put to the project owner before Stage 1 implementation began. The
chosen answers are recorded here and are what Stage 1 implements; they are part of the wire/trust
contract from the first shipped hybrid bundle onward.

1. **Verifier OS floor — DECIDED: (A) classical fallback + loud log.** When `MLDsa.IsSupported`
   is false on the verifying machine (old Windows / CNG without ML-DSA), a hybrid signer's
   classical ECDSA-P256 signature is verified alone and a loud log/event records that PQ
   verification was skipped due to OS capability. This is sound only because `MLDsa.IsSupported`
   reflects the real platform and cannot be influenced by bundle content. On a capable OS the
   companion rule is strictly enforced (INT011 on any missing/invalid PQ companion).
2. **Ship now vs design-only — DECIDED: ship Stage 1 now.**
3. **Context string — DECIDED: `"falkforge/manifest"`**, frozen forever. Defined once as a
   constant (`SignatureAlgorithms.ManifestContext`) shared by signer and verifier.
4. **Default parameter set — DECIDED: ML-DSA-65.** The `algorithm` field carries the parameter
   set name on the wire, so ML-DSA-87 (or -44) remains possible later without a wire change;
   65 is the default the shipped signer and tests use.
5. **Mixed-set posture — DECIDED: build-time WARNING (not hard-fail)** when a baked/registered
   trusted set mixes companioned and un-companioned classical keys (an un-companioned key is the
   quantum weakest link). Surfaced both as an MSBuild warning from the `TrustedKeys.targets`
   generator and via `EngineTrustAnchor.ConfigurationWarnings`.
6. **SignServer PQ**: deferred to Stage 4; availability must be assessed, not assumed.
7. **Ephemeral (zero-config) builds — DECIDED: yes, hybrid.** The ephemeral signer emits a
   classical + ML-DSA-65 pair (when the build OS supports ML-DSA), keeping dev-loop parity with
   the production envelope shape.
8. **CLI/decompiler inspection-grade paths** pass an empty trust set today ⇒ no companions ⇒ PQ entries are hygiene-checked but never required there. Acceptable (they're inspection-grade by design), but note it in docs.

## Appendix — probe artifacts (scratchpad only, disposable)

- `scratchpad\mldsa-probe\` — console probe (source above the design's claims; run under CoreCLR and as NativeAOT exe at `aot-out\mldsa-probe.exe`).
- `scratchpad\mldsa-gate\` — pragma-free compile proving no `SYSLIB5006` experimental opt-in is required on .NET 10 GA.
- Nothing in the repo was modified; no git operations performed.
