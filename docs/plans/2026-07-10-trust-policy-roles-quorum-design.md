# Key Roles, M-of-N Quorum, Signed Per-Operation Policy & Signature Expiry Design

Status: **DRAFT 2026-07-10.** **Stage 1 landed 2026-07-10** (branch `feature/c19-roles-quorum`): `TrustRole` roles on the baked + code-registered trusted sets (`BakedTrustedKeys.Roles`, `EngineTrustAnchor.EffectiveRoles`), the collect-all-distinct `CollectTrustedSignatures` + distinct-key `QuorumEvaluator`, the baked default policy table + operation resolution (`BakedTrustPolicy`), and the new **INT010** quorum-unsatisfied code, wired into `PayloadIntegrityGate` (Install) and the update path (`SignedPayloadTocVerifier`/`StagedUpdateVerifier`). Backward-compatible: with no roles configured the gates take the exact C14 verify-any path. Stage-1 deviations from this design: the baked policy is a code constant (`BakedTrustPolicy.Default`), not an MSBuild `<FalkForgeTrustPolicy>`-generated file (§4.1's "or an inline default"); operation resolution for the update path lives in the protocol verifier (which is where `BakedTrustPolicy` already lives) rather than being pre-resolved in the engine, because the signed epoch is only available after extraction. Stages 2 (bundle-carried tighten-only policy) and 3 (per-signature expiry) remain unimplemented.

Scope: upgrade FalkForge's trust layer from "a flat trusted set, accept if ANY one trusted key signs" (shipped in C14) to "trusted keys carry roles, and a signed per-operation policy requires specific role combinations and M-of-N thresholds per operation," plus per-signature validity windows (expiry, feature request #19). Design only — no code in this document. Implementation is split into three stages (§9).

This document extends, and does not supersede, the C14 design `docs/plans/2026-07-09-bundle-trust-signing-rotation-design.md`. It assumes the C14 machinery is shipped: the v2 multi-signature envelope (`ManifestSignatureEnvelope` with `signatures[]`, `epoch`, `revoked`), the byte-identical-across-versions signed-bytes contract (`IntegrityEnvelopeCodec.ComputeSignedBytes`), the baked trusted set (`TrustedKeys.targets` → `BakedTrustedKeys.Fingerprints`), the code-path union (C18 `EngineTrustAnchor.EffectiveFingerprints`), the two verify gates (`PayloadIntegrityGate`, `SignedPayloadTocVerifier`) with the shared `BundleTrustVerifier`/`StagedUpdateVerifier` entry points, and the dormant anti-downgrade/revocation store (`TrustState`/`TrustStateStore`, C16 activation pending). Every type and line reference below is to the currently-shipped `main` (ignoring the in-flight C17 SignServer work).

This work is what turns "no single key compromise can silently ship a high-risk change" from an operational aspiration into an enforced invariant.

---

## 1. Problem statement

### 1.1 What C14 established, and its residual weakness

C14 closed the trust-anchor hole: a signature is trusted only when its key's fingerprint is in a set pinned *outside* the bundle (the engine's baked/registered set, `EngineTrustAnchor.EffectiveFingerprints`). The verify rule is **verify-any** — `IntegrityEnvelopeCodec.MatchTrustedSignature` (`IntegrityEnvelopeCodec.cs:265-328`) walks the signatures and returns success "as soon as **one** signature both (a) re-derives to its declared fingerprint, (b) has that fingerprint in the trusted set, and (c) cryptographically verifies" (`IntegrityEnvelopeCodec.cs:233-243`). This is a logical **1-of-N OR** over the trusted set.

Verify-any is correct and necessary for rotation (during a dual-sign overlap a bundle carries old+new signatures and an engine trusting either key must accept — C14 §7.2). But it means the trust decision collapses to: *does any single trusted key vouch for this bundle?* Every trusted key is fungible and omnipotent. The blast radius of one key is the entire product.

### 1.2 Threat: a single compromised trusted key ships anything

If **any one** key in `EffectiveFingerprints` is compromised — a leaked CI signing secret, a developer's key that was baked in for convenience, a vault mis-permission, a malicious insider with signing access — the attacker can sign a fully rewritten update, and every C14 gate passes:

- `MatchTrustedSignature` accepts on that one signature (`IntegrityEnvelopeCodec.cs:308-317`).
- `PayloadIntegrityGate.Verify` accepts (`PayloadIntegrityGate.cs:82-84`).
- `SignedPayloadTocVerifier.Verify` / `StagedUpdateVerifier` accept require-signed over the baked set.
- The attacker can even author a `revoked: [<other publisher key>]` entry (`ManifestSignatureEnvelope.Revoked`) and `epoch` bump, because one trusted key currently authorizes **revocation** too — a single compromised key can evict the *legitimate* keys.

This is the classic "one key to rule them all." The industry answer is well established (TUF role separation, Sigstore/Fulcio threshold roots, Debian's archive-vs-upload key split, Windows dual-signing for kernel drivers): **separate keys by role, and require a quorum of distinct roles for high-risk operations.** No single key — however privileged — can unilaterally ship a key change, a downgrade, or a revocation.

### 1.3 Threat: no signature ages out

C14 signatures never expire. A key that signed a release in 2026 produces a signature that verifies forever (as long as its fingerprint stays trusted). Combined with the still-dormant epoch store (C16), a leaked key's *old* signatures remain replayable indefinitely on any engine that never advanced its epoch. A per-signature validity window (#19) bounds this: a signature made with key K is only honored inside K's declared `[notBefore, notAfter]`, so retiring a key naturally ages out its signatures even before revocation propagates.

### 1.4 Goal

1. **Role-tag** each trusted key (a key may hold several roles).
2. **A signed, tamper-proof, pinned policy** maps each *operation* (install / update / key-change / downgrade / revoke) to a **required-roles + minimum-distinct-count** rule, supporting general **M-of-N thresholds** and **role-combination (AND)** requirements.
3. **Evolve verify** from "first valid trusted signature wins" to "collect **all** valid **distinct** trusted signatures, resolve each to its key's roles, and evaluate the set against the operation's policy — fail loud if unsatisfied."
4. **Per-signature expiry** windows, folded into the signed bytes exactly like C14 folds `epoch`/`revoked`, byte-identical to legacy in the neutral (no-window) case.
5. **Total backward compatibility.** With no roles and no policy configured, the effective behavior is *exactly* C14's "1 signature from any trusted key." Publishers opt in to stricter governance. Every already-signed bundle and every already-installed engine keeps working.

### 1.5 In scope / out of scope

**In scope:** role tagging of the baked and code-registered trusted sets; the signed per-operation policy model, its location, pinning, and anti-rollback; the collect-all-distinct verify algorithm and quorum evaluation; per-signature validity windows and their clock; meshing with rotation (C14 §7), the epoch/revocation store (C16), and the v1/v2 envelope compat contract.

**Out of scope (unchanged from C14 §2 unless noted):** extracting the private signing keys (assumed hard); rewriting the baked engine binary (the pinned policy and roles live in the same channel the attacker cannot rewrite); forging ECDSA-P256/SHA-256. Also out of scope and deferred to §10: publisher-identity-vs-key binding (#1), a transparency log (#17), engine runtime self-attestation (#9), and general OTA delivery of *new* trusted keys/policy without an engine rebuild.

---

## 2. Threat model (delta over C14 §2)

The C14 attacker capabilities carry over verbatim: full artifact rewrite, update-feed MITM/poisoning, replay/downgrade. This design adds one attacker capability to the "in scope" list and one assumption to the "hard" list:

**Newly in scope — a single compromised trusted key.** The attacker holds the private key for **exactly one** fingerprint in `EffectiveFingerprints` (leaked CI key, one compromised signer, one malicious insider). The design's central guarantee is that this attacker **cannot** ship a key-change, downgrade, or revocation, and (under a strict publisher policy) cannot ship an install/update either if that operation's threshold exceeds 1. They can still sign bundles that any 1-of-N operation permits — a strict publisher raises even install/update to ≥2, at the cost of always dual-signing.

**Assumed hard (out of scope).** The attacker cannot compromise a **quorum** of distinct-role private keys simultaneously (e.g. both a `release` key *and* a `recovery` key held in separate custody / separate HSMs). If they can, quorum provides no benefit — the whole scheme rests on the keys for different roles being independently protected. This is the standard threshold-trust assumption and is the publisher's operational responsibility (§8).

Everything the pinned-policy layer relies on — the baked engine binary, the pinned trusted set, the pinned policy — remains in the one channel C14 §2.2 assumes the attacker cannot rewrite. The signed-bytes/envelope crypto assumptions are unchanged.

---

## 3. Key roles

### 3.1 The role set

A trusted key is tagged with one or more roles from a small, fixed, versioned enum. The set is deliberately minimal — every role must map to a genuinely different custody model or blast radius, or it is noise.

| Role | Held by / custody | Authorizes | Rationale |
|------|-------------------|------------|-----------|
| `release` | Release manager / release HSM. The everyday signing identity. | Ordinary install + update signing. | The workhorse. Under default policy this is the only role that matters, so C14 flat behavior is preserved (§7.1). |
| `recovery` | Offline / cold-storage key, separate custody from `release`. Rarely used. | Co-signs key-change / rotation (with `release`). Second factor for the highest-risk operation. | Rotation is where a single-key compromise is most dangerous (it re-anchors trust). Requiring an offline second key here means a compromised online `release` key cannot rotate trust to an attacker's key. |
| `security` | Security team key, separate custody. | Co-signs downgrade; authors/co-signs revocation. | Downgrade and revocation are governance actions, not release actions — they belong to whoever owns incident response, not the release pipeline. |
| `emergency-revoke` | Break-glass key, offline, tightly held, single purpose. | Alone-sufficient to author a `revoked[]` entry (revoke-only). | During an active compromise the security team must be able to evict a leaked key fast, possibly when the normal quorum is unreachable. Scoped to *only* revocation so a break-glass key leak cannot itself ship code. Optional — a publisher may fold this into `security`. |
| `ci` | Automated build pipeline key. | Signs nightly / pre-release / channel-restricted builds. Explicitly **not** `release`. | Distinguishes "a machine signed this" from "a human release process signed this," so a compromised CI runner cannot ship to the stable channel if policy demands `release` there. |
| `developer` | Individual developer keys (many). | Local/dev-channel bundles only; never satisfies a production operation. | Lets developers produce locally-trusted test bundles without their key ever counting toward a real install/update quorum. A `developer`-only bundle fails every production policy rule. |
| `timestamp` | Timestamp authority key (may be a third party). | Attests *when* a signature was made (feeds expiry, §5). Never counts toward an operation quorum. | Expiry windows are only meaningful with a trusted "as-of" time. A `timestamp` signature is evidence of time, not of authorship, so it is excluded from every operation's required-role counting. |

Roles are represented as a `[Flags]`-style set per key (a key can be `release | recovery`). The enum is closed and versioned; unknown role tokens encountered at parse time are **ignored** (forward-compat: a newer publisher's policy naming a role an older engine doesn't know simply contributes nothing, never crashes — see §6.4).

`timestamp` and `developer`/`ci` never satisfy production operations; they exist so a key can be *trusted for its narrow purpose* without being *release-omnipotent*. This is the whole point of roles: trust is no longer monolithic.

### 3.2 Tagging a baked key with roles

Extend the C14 injection channels from `fingerprint` to `fingerprint → roles`:

**MSBuild (`TrustedKeys.targets`).** Today `<FalkForgeTrustedKey Include="A1B2..." />` carries a bare fingerprint (`TrustedKeys.targets:27-29`). Add an optional `Roles` metadata:

```xml
<FalkForgeTrustedKey Include="A1B2C3..." Roles="release" />
<FalkForgeTrustedKey Include="D4E5F6..." Roles="recovery;security" />
<FalkForgeTrustedKey Include="0789AB..." />   <!-- no Roles → default role, §7.1 -->
```

The `_WriteFalkTrustedKeysSource` RoslynCodeTaskFactory task (`TrustedKeys.targets:31-94`) is extended to emit, alongside the existing `BakedTrustedKeys.Fingerprints` `FrozenSet<string>`, a `BakedTrustedKeys.Roles` `FrozenDictionary<string, TrustRole>` (fingerprint → parsed role flags). The existing fingerprint normalization (strip separators, uppercase, hex-only — `TrustedKeys.targets:64-77`) is reused; roles are split on `;`/`,`, matched case-insensitively against the `TrustRole` enum names, unknown tokens dropped. A fingerprint with no `Roles` metadata is emitted with the default role (§7.1). `FrozenDictionary`/`FrozenSet` satisfy Gate 6 (allocation-free static lookup).

**Code path (`EngineTrustAnchor`, C18).** The registration methods gain a roles parameter, additive to the existing signatures:

```csharp
EngineTrustAnchor.TrustFingerprint(string fingerprint, TrustRole roles = TrustRole.Release);
EngineTrustAnchor.TrustPublicKey(ReadOnlySpan<byte> spki, TrustRole roles = TrustRole.Release);
EngineTrustAnchor.TrustPublicKeyPem(string pem, TrustRole roles = TrustRole.Release);
```

The freeze-once discipline (`EngineTrustAnchor.cs:54-74`) is unchanged. The union computed on first read (`EngineTrustAnchor.cs:67-69`) now produces **two** frozen structures: `EffectiveFingerprints` (unchanged — the set every C14 verifier already reads) **and** a new `EffectiveRoles` `FrozenDictionary<string, TrustRole>` merging baked roles with code-registered roles. When the same fingerprint is registered by both channels (or twice), the roles are **unioned** (OR of the flags) — additive, matching the anchor's "never a replacement" contract (`EngineTrustAnchor.cs:12-13`).

The default value on all overloads is `TrustRole.Release`, so **every C18 caller that exists today keeps compiling and keeps meaning exactly what it meant** (a plain trusted key = a release key), and the default policy (§7.1) then treats it identically to C14 verify-any.

### 3.3 The `TrustRole` type

A new `[Flags] enum TrustRole` in `FalkForge.Engine.Protocol.Integrity` (so both the verifier in Engine and any future policy-in-protocol type can see it; roles are part of the trust *vocabulary*, not engine-private policy):

```csharp
[Flags]
public enum TrustRole
{
    None            = 0,
    Release         = 1 << 0,
    Recovery        = 1 << 1,
    Security        = 1 << 2,
    EmergencyRevoke = 1 << 3,
    Ci              = 1 << 4,
    Developer       = 1 << 5,
    Timestamp       = 1 << 6,
}
```

Flags enum = a key's roles are one `int`, allocation-free, trivially AOT-safe, and role membership is a bit test. The wire/text form (MSBuild metadata, policy JSON, `EngineTrustAnchor` calls) uses the role **names**, parsed to flags at build/bootstrap time — names never travel inside a signed bundle's per-signature entries (roles are resolved from the *pinned* trusted set at verify time, never from the bundle; §4).

**Roles are never read from the bundle.** A `SignatureEntry` (`SignatureEntry.cs`) is unchanged — it carries no role field. At verify time the engine resolves each accepted fingerprint to its roles via `EffectiveRoles`, the pinned structure. Putting a role claim inside the bundle would reopen the C14 self-describing-trust hole (a key asserting its own privilege). Trust and role both come from outside the bundle, always.

---

## 4. Where the signed per-operation policy lives (the central decision)

The policy that maps operation → required roles/threshold **must itself be tamper-proof and pinned**, or an attacker just rewrites "key-change needs release+recovery" down to "key-change needs 1 developer." Two candidate homes, and the recommendation.

### 4.1 Option A — bake the policy into the engine (RECOMMENDED, Stage 1)

The policy is a build-time input to `FalkForge.Engine`, exactly like the trusted set, emitted as a generated constant and covered by the engine binary's own integrity (the channel §2.2 assumes the attacker cannot rewrite). A new `<FalkForgeTrustPolicy Include="policy.json" />` MSBuild item (or an inline default) is read by an extension of `TrustedKeys.targets` and emitted as a generated `BakedTrustPolicy` (a `FrozenDictionary<OperationKind, PolicyRule>`), source-gen only.

- **Pro:** no separate signature, no pinning machinery, no new trust-anchor to reason about — the policy is as trustworthy as the engine the user already chose to run (C14 §3.3 self-consistency argument applies unchanged). It cannot be stripped (there is nothing in the bundle to strip) or rolled back (it is code). Simplest correct thing.
- **Con:** changing the policy requires an engine rebuild+reship — the same cadence as changing the trusted set or roles, which already require a rebuild. For a governance policy that changes about as often as the key roster, this is acceptable.

### 4.2 Option B — a separate signed policy artifact carried in the bundle (Stage 2, opt-in evolution)

A `signed-policy.json` blob embedded in the bundle (or the manifest), itself signed by a quorum, letting a publisher tighten policy OTA without an engine rebuild.

- **Pro:** policy can evolve between engine releases.
- **Con:** it needs its own trust anchor. Which role signs the policy? How does the engine pin it? How is policy-rollback (serve last week's looser policy) prevented? Each answer adds machinery and a new hole to get wrong.

### 4.3 Recommendation

**Bake the policy (Option A) as the authoritative default; add the bundle-carried signed policy (Option B) in Stage 2 as a strictly-tightening, anti-rollback overlay — never a replacement.** Concretely:

1. **The baked policy is the floor.** The engine always has a baked `BakedTrustPolicy` (defaulting to the C14-equivalent "1 of any trusted key for everything," §7.1). It can never be loosened from outside.
2. **A bundle-carried signed policy may only *raise* requirements.** For each operation the *effective* rule is `max(baked, carried)` — carried can add roles or raise the threshold, never drop below the baked floor. So a loosened forged policy is a no-op; a stripped carried policy falls back to the (safe) baked floor. This dissolves the "which role signs the policy, and can that be forged down" problem: the worst a policy-signature forgery achieves is *no relaxation*.
3. **The carried policy is signed to a bootstrap quorum and version-pinned.** It must be signed by the same quorum the baked policy demands for a `key-change` (release + recovery), verified against the pinned trusted set + roles (reusing §5's evaluator), and carries a monotonic `policyEpoch` recorded in the trust store (§6) so an older, looser carried policy cannot be replayed. Because it can only tighten, even a flawed pin degrades safely to the baked floor.

**The single most important open decision** is therefore resolved as: *the policy is pinned by baking it into the engine (same channel as the trusted set); the optional bundle-carried policy is trusted only to **tighten** the baked floor, is quorum-signed and version-pinned, and can never relax trust.* Stage 1 ships only the baked policy; Stage 2 adds the tighten-only overlay. This is a deliberate, conservative choice — it keeps the security-critical path (can policy be weakened?) provably answerable ("no, ever") while still allowing OTA hardening.

---

## 5. The policy model and operation set

### 5.1 `PolicyRule` and `OperationKind`

```csharp
public enum OperationKind
{
    Install,      // fresh install (bootstrapper / --extract of embedded self)
    Update,       // routine update, same key-epoch
    KeyChange,    // rotation: envelope.epoch > stored epoch
    Downgrade,    // an explicitly-permitted lower version (distinct from a blocked replay)
    Revoke,       // the envelope declares revoked[] fingerprints
}

// A single AND-requirement: at least Count DISTINCT trusted keys each holding Role.
public readonly record struct RoleRequirement(TrustRole Role, int Count);

// One operation's rule: every RoleRequirement must be satisfiable simultaneously by
// DISTINCT keys, and the total distinct signatures must be >= MinDistinctSignatures.
public sealed record PolicyRule(
    IReadOnlyList<RoleRequirement> Requirements,   // AND-combined
    int MinDistinctSignatures);                    // overall M-of-N floor
```

`RoleRequirement(Release, 1)` = "≥1 distinct release key." A rule with two requirements `[(Release,1),(Recovery,1)]` = "≥1 release key AND ≥1 recovery key, held by **distinct** keys." `MinDistinctSignatures` expresses a bare M-of-N with no role constraint (e.g. "any 2 trusted keys") and also enforces the distinct-key floor when role requirements overlap.

### 5.2 The default policy table (baked)

The recommended baked default for a publisher who opts into governance (the ship-with-nothing default is the even weaker C14-equivalent, §7.1):

| Operation | Verify-time signal | Requirements (AND, distinct keys) | Min distinct sigs |
|-----------|--------------------|-----------------------------------|-------------------|
| `Install` | fresh-install path (`requireSigned` false) | `(Release, 1)` | 1 |
| `Update` | update path, `envelope.epoch == stored` | `(Release, 1)` | 1 |
| `KeyChange` | update path, `envelope.epoch > stored` | `(Release, 1)`, `(Recovery, 1)` | 2 |
| `Downgrade` | explicitly-permitted lower version | `(Release, 1)`, `(Security, 1)` | 2 |
| `Revoke` | `envelope.revoked` non-empty | `(Security, 1)` **or** `(EmergencyRevoke, 1)`, plus `(Release, 1)` | 2 |

Notes:
- `KeyChange` (rotation) is the highest-risk everyday operation and demands the offline `recovery` key precisely so a compromised online `release` key cannot re-anchor trust alone (§2 assumed-hard).
- `Revoke` is expressed as `(Release,1)` AND (`Security` OR `EmergencyRevoke`)`,1)`. The `OR` inside one requirement is modeled by allowing a `RoleRequirement.Role` to be a **flag union** (`Security | EmergencyRevoke`) — a key satisfies it if it holds *any* named bit. So the requirement becomes `[(Release,1),(Security|EmergencyRevoke,1)]`. This keeps the model to a flat AND-of-requirements while still expressing role-OR within a requirement. Emergency break-glass (revoke authored by `emergency-revoke` alone, no `release`) is a *separate, looser* publisher opt-in rule, not the default — a publisher who wants true break-glass sets `Revoke = [(EmergencyRevoke,1)]`.
- General M-of-N (e.g. "3-of-5 release keys") is `[(Release,3)]` with `MinDistinctSignatures = 3`.

### 5.3 Resolving the operation at verify time

The operation is **not** read from the bundle — it is derived from the verify context and the signed envelope fields, reusing signals C14 already computes:

1. **Path** decides `Install` vs `Update` family: the fresh-install gates (`PayloadIntegrityGate` via `ApplyStep`, and `SignedPayloadTocVerifier` on the bootstrapper/`--extract` path in `Program.cs`) resolve `Install`; the update path (`StagedUpdateVerifier.VerifyWithBakedTrust`, `SignedPayloadTocVerifier.cs` under `requireSigned: true`) resolves the `Update` family. This is exactly the fresh-vs-update asymmetry C14 §3.4 already threads via `TrustPolicy.RequireSigned`.
2. Within the `Update` family, the **signed epoch** distinguishes: `envelope.Epoch > storedEpoch` → `KeyChange` (rotation); `envelope.Epoch == storedEpoch` → `Update`; `envelope.Epoch < storedEpoch` is already rejected as a replay by INT008 (`SignedPayloadTocVerifier.cs:130-133`) **before** policy runs — an *intentional* downgrade is a distinct, explicitly-requested path (a `Downgrade` verify call the UI/operator initiates), never inferred from a low epoch.
3. **`envelope.Revoked` non-empty** overlays the `Revoke` requirement: any bundle that declares revocations must additionally satisfy the `Revoke` rule (so a lone `release` key cannot author a revocation). `Revoke` requirements are AND-merged with the base operation's (a rotation that also revokes must satisfy `KeyChange` ∪ `Revoke`).

Because epoch and revoked are already in the signed bytes (C14 §6.3, `IntegrityEnvelopeCodec.cs:60-85`), the operation classification is itself tamper-proof: an attacker cannot relabel a `KeyChange` as an `Install` to dodge the stricter rule without breaking the signature (epoch is signed) or landing on the fresh-install path (which they only reach if the *user* runs their artifact — outside the model, C14 §3.3).

### 5.4 The quorum evaluator

Given the set `S` of **valid, distinct, trusted, in-window** signatures (from §6.2) each resolved to a `TrustRole`, and a `PolicyRule`, the rule is satisfied iff:

1. `|S| >= rule.MinDistinctSignatures`, **and**
2. there exists an assignment of *distinct* keys in `S` to the requirements such that each `RoleRequirement(role, count)` is met by `count` distinct keys each holding (any bit of) `role`, **no key used for more than one requirement**.

Condition 2 is a bipartite matching (keys ↔ requirement-slots). With the tiny cardinalities here (≤ a handful of requirements, each count small), a greedy assignment with backtracking, or a direct Hall's-condition check over the requirement power-set, is correct and allocation-light — no LINQ in the hot path (Gate 6); iterate arrays and a `stackalloc`/`Span<bool>` "used" mask. Distinct-key enforcement is the crux: **a single key holding `release | recovery` does NOT satisfy both `(Release,1)` and `(Recovery,1)` alone** — that would defeat quorum. The matching forbids reusing one key across two requirements, so `KeyChange` genuinely needs two *different* private keys.

Determinism: `S` is deduplicated by fingerprint (uppercase hex) so a bundle that (maliciously or accidentally) repeats the same key twice contributes **one** member. This is the distinct-key guarantee the design hinges on.

---

## 6. Verify algorithm change

### 6.1 From `MatchTrustedSignature` (first-wins) to `CollectTrustedSignatures` (collect-all-distinct)

C14's `MatchTrustedSignature` returns on the first valid trusted signature (`IntegrityEnvelopeCodec.cs:308-317`). It is **kept** (revocation enforcement and the simplest 1-of-N default still use it), and a sibling is added:

```csharp
// New in IntegrityEnvelopeCodec. Collects EVERY valid, trusted, DISTINCT signature,
// resolving each to its pinned role via the caller-supplied resolver. Never short-circuits.
public static Result<IReadOnlyList<TrustedSignature>> CollectTrustedSignatures(
    ManifestSignatureEnvelope envelope,
    IReadOnlySet<string> trustedFingerprints,
    Func<string, TrustRole> roleOf,           // fingerprint → pinned roles (EffectiveRoles lookup)
    long verifyUnixTime);                     // for the expiry window check, §7 / §5

public readonly record struct TrustedSignature(string Fingerprint, TrustRole Roles);
```

Algorithm (mirrors `MatchTrustedSignature`'s per-entry checks `IntegrityEnvelopeCodec.cs:283-323`, but accumulates instead of returning):

1. Compute `hash = SHA-256(ComputeSignedBytes(files, epoch, revoked))` **once** (`IntegrityEnvelopeCodec.cs:280`), unchanged — the signed bytes already cover epoch+revoked, and §7 adds the validity window to them.
2. `seen = new HashSet<string>(OrdinalIgnoreCase)` for distinct-key dedup.
3. For each `SignatureEntry`:
   a. Re-derive fingerprint from `publicKey`; skip on mismatch (lying fingerprint — `IntegrityEnvelopeCodec.cs:303-305`).
   b. Skip if not in `trustedFingerprints` (untrusted key — `:308-309`). *(Empty-set consistency-only mode is **not** used for quorum — quorum requires named roles, which require a pinned set. On the require-signed path an empty set is already INT009 fail-closed, `SignedPayloadTocVerifier.cs:113-117`.)*
   c. **Validity window (§7):** skip if `verifyUnixTime` is outside `[notBefore, notAfter]` for this entry.
   d. `VerifyHash`; on success and if `seen.Add(fp)` (first occurrence of this key), append `TrustedSignature(fp, roleOf(fp))`.
4. Empty result → INT003 (no usable signatures) if the envelope had none, else the caller maps "collected but empty" to the policy failure below. Return the collected list.

`CollectTrustedSignatures` does **not** itself decide acceptance — it returns the evidence. The gate then runs the §5.4 evaluator against the operation's `PolicyRule`.

### 6.2 Gate flow (both `PayloadIntegrityGate` and `SignedPayloadTocVerifier`)

The gates gain the policy + role resolver (threaded via the enriched `TrustPolicy`, §6.3). New flow, inserted where they currently call `VerifyTrusted`/`MatchTrustedSignature` (`PayloadIntegrityGate.cs:82`, `SignedPayloadTocVerifier.cs:123`):

1. Presence + INT009 fail-closed checks — **unchanged** (`PayloadIntegrityGate.cs:50-78`, `SignedPayloadTocVerifier.cs:92-117`).
2. Parse envelope — unchanged.
3. **Resolve `OperationKind`** from path + epoch + revoked (§5.3).
4. `collected = CollectTrustedSignatures(envelope, trusted, EffectiveRoles.Lookup, now)`.
5. **Anti-downgrade / revocation** — unchanged and run first (INT008 epoch `SignedPayloadTocVerifier.cs:130-133`; revoked-key rejection `:138-141`). A revoked key is dropped from `collected` *before* quorum counting so a revoked key can never count toward a threshold.
6. **Evaluate** `collected` against `EffectiveTrustPolicy[operation]` (§5.4). Unsatisfied → **INT010** (fail loud, with a message naming the operation, the requirement, and what was present — e.g. "key-change requires ≥1 release AND ≥1 recovery signature; found: 1 release, 0 recovery").
7. Coverage / byte-binding loops (`PayloadIntegrityGate.cs:88-114`, `SignedPayloadTocVerifier.cs:151-178`) — **unchanged**.

The **backward-compat default** (§7.1): when the effective policy for the operation is the C14-equivalent `(AnyTrusted, 1)`, step 6 collapses to "≥1 valid trusted distinct signature," which is *exactly* what `MatchTrustedSignature` accepted. Implementation may even short-circuit to the existing `MatchTrustedSignature` when the rule is the trivial 1-of-any, keeping the C14 code path bit-for-bit for un-migrated publishers.

### 6.3 `TrustPolicy` grows (Engine-internal)

The C14 `TrustPolicy` readonly struct (`TrustPolicy.cs:21-48`) carries `TrustedFingerprints` + `RequireSigned`. Extend it (still Engine-internal — `Engine.Protocol` cannot see it, so the protocol-level `SignedPayloadTocVerifier` keeps taking raw args, as C14 §Stage-1 note records):

```csharp
internal readonly struct TrustPolicy
{
    public IReadOnlySet<string> TrustedFingerprints { get; }
    public bool RequireSigned { get; }
    public IReadOnlyDictionary<string, TrustRole> Roles { get; }        // NEW — EffectiveRoles
    public IReadOnlyDictionary<OperationKind, PolicyRule> Rules { get; } // NEW — effective (baked ⊔ carried)
    // ConsistencyOnly / FreshInstall factories extended to pass empty Roles + the default rule table.
}
```

`SignedPayloadTocVerifier.Verify` (`SignedPayloadTocVerifier.cs:77-84`) grows two parameters — a `roleOf` resolver (or the role dictionary) and the resolved `OperationKind` (or the `PolicyRule` directly, keeping the protocol layer free of the operation-resolution logic, which stays in Engine). Defaults preserve the current signature's behavior: no roles + the trivial rule = C14 verify-any.

### 6.4 Forward/back compat of unknown roles & operations

- An **older engine** reading a bundle signed by keys it knows only as `release` (because its pinned `EffectiveRoles` predates a role split) evaluates against its own baked policy — it never learns roles from the bundle, so there is no cross-version role confusion.
- A **newer policy** (bundle-carried, Stage 2) naming an `OperationKind` or `TrustRole` an older engine's enum lacks: unknown role tokens parse to `None` (contribute nothing, §3.1) and unknown operation keys are ignored (the baked rule for known operations still applies) — fail-safe toward the stricter baked floor, never a crash, never a silent relaxation.

---

## 7. Backward compatibility & migration

### 7.1 The default is C14 exactly

The ship-with-nothing behavior must be identical to today:

- **No roles configured:** every trusted key (baked or code-registered) defaults to `TrustRole.Release` (§3.2 default parameter, MSBuild default emission).
- **No policy configured:** `BakedTrustPolicy` defaults to, for *every* `OperationKind`, the rule `PolicyRule([(AnyRole, 1)], MinDistinctSignatures: 1)` — where `AnyRole` is a sentinel (all bits set / a dedicated "any trusted key" flag the evaluator treats as "role-agnostic"). This is literally "≥1 valid trusted distinct signature," i.e. C14 verify-any.
- Result: an un-migrated publisher's engine behaves bit-for-bit as C14. `MatchTrustedSignature`, the INT001/003/007/008/009 codes, the epoch/revocation store, and every existing test path are untouched on this default. The new INT010/INT011 codes only fire once a publisher configures a non-trivial rule or a validity window.

### 7.2 Opting in

A publisher migrates incrementally:
1. Tag keys with roles (MSBuild `Roles=` / `EngineTrustAnchor` overloads). **Correction (post-Stage-1 review): this step is NOT behavior-neutral.** The Stage 1 implementation does not wait for a publisher to separately "set a policy" — the moment `EffectiveRoles` is non-empty (any key carries an explicit role), the BAKED default table (`BakedTrustPolicy.Default`) engages automatically for every operation, including `Install`/`Update`, which require one `Release` signature. So tagging a key with a non-`Release` role takes effect immediately, on the very next verify, not only once a stricter policy is later "named." If none of the tagged keys hold `Release`, `Install`/`Update` become permanently unsatisfiable (a total self-lockout) as soon as roles are tagged. `EngineTrustAnchor.Freeze` now guards against this: it throws at bootstrap if roles are configured with no `Release`-holding key, and emits a non-fatal `ConfigurationWarnings` entry if a `Release` key exists but no key holds `Recovery` (which would make a future `KeyChange` unsatisfiable). Publishers should always tag at least one key `Release` (or leave it un-roled, which defaults to `Release`, §7.1) in the same change that introduces any other role.
2. Set a baked policy raising `KeyChange`/`Downgrade`/`Revoke` first (the highest-value, lowest-friction wins — those operations are rare, so demanding a quorum for them costs little).
3. Optionally raise `Install`/`Update` to ≥2 (this forces routine dual-signing — real operational cost, opt in deliberately).
4. Optionally add validity windows (§8) and, in Stage 2, a carried tighten-only policy.

Every step is backward compatible: old engines (older baked policy) simply enforce less; **already-signed bundles keep verifying** because the signed bytes are unchanged unless the publisher adds a window/epoch/revoked (all of which fold in only when present, C14 §6.3). A bundle dual-signed for a strict engine still satisfies a lax engine's 1-of-any rule (extra signatures never hurt).

### 7.3 Rotation meshes with quorum (C14 §7)

The C14 rotation runbook ("trust leads, signing follows," dual-sign overlap) is unchanged, with one refinement: a `KeyChange` operation now *requires* the quorum, which **is** the dual-sign. During rotation the bundle is signed by `release` (old) + `recovery` (co-signing the epoch bump), satisfying `KeyChange = [(Release,1),(Recovery,1)]`. The C14 anti-downgrade epoch and the C16 revocation store are the *enforcement* of the epoch signal that classifies `KeyChange` vs `Update` (§5.3) — this design consumes those signals, it does not duplicate them. Emergency revocation (C14 §7.3) now routes through the `Revoke` rule: a break-glass `emergency-revoke` key can author the revocation if the publisher opted into the looser break-glass rule; otherwise revocation needs the `security`+`release` quorum.

---

## 8. Signature expiry (feature #19)

### 8.1 Per-signature validity window

Expiry is **per-signature**, not per-envelope — each key's signature carries its own `[notBefore, notAfter]`, so a key being retired ages out its signatures independently while a co-signer's stay valid. Extend `SignatureEntry` (`SignatureEntry.cs`) with two optional fields:

```csharp
[JsonPropertyName("notBefore")] [JsonIgnore(Condition = WhenWritingDefault)]
public long NotBefore { get; set; }   // Unix seconds; 0 = unbounded below

[JsonPropertyName("notAfter")]  [JsonIgnore(Condition = WhenWritingDefault)]
public long NotAfter { get; set; }    // Unix seconds; 0 = unbounded above
```

Unix seconds (int64), not ISO-8601 strings, so the signed representation is a canonical integer with no formatting ambiguity (offline-deterministic signed bytes). `0/0` = no window = the neutral case.

### 8.2 Folded into the signed bytes (byte-identical neutral case)

The window must be signed or an attacker extends `notAfter`. Fold it into `ComputeSignedBytes` using the **exact C14 U+001F pattern** (`IntegrityEnvelopeCodec.cs:60-85`). But note: C14's `ComputeSignedBytes(files, epoch, revoked)` signs *envelope-level* epoch/revoked, while the window is *per-signature*. So the window is bound at the **per-signature** layer:

Each entry's signed message becomes `SHA-256( ComputeSignedBytes(files, epoch, revoked) ‖ windowSuffix(entry) )`, where `windowSuffix` is **empty** when `notBefore == 0 && notAfter == 0` (→ byte-identical to the C14 message, so every existing v1/v2 signature still verifies) and otherwise `U+001F "nb=" notBefore U+001F "na=" notAfter`. Because different entries can have different windows, each signature is over a *slightly* different message when windowed — this is fine (each signer signs its own entry's window) and requires the signer to compute per-entry hashes when any window is set, but the common no-window path still hashes once for all entries (the C14 fast path, `IntegrityEnvelopeCodec.cs:280`). The verifier already imports+verifies per entry, so per-entry hashing on the windowed path is a natural extension, not a new loop.

This keeps the **neutral case byte-identical to legacy** (the property C14 depends on for v1/v2 compat) while making any declared window unforgeable.

### 8.3 Which clock — the hard question

A validity window needs a "now," but the engine values offline determinism and must resist a rolled-back OS clock (an attacker who controls the machine clock could otherwise resurrect an expired-key signature by setting the clock back). Resolution — a **monotonic seen-time high-water mark**, layered:

1. **Primary check — verify-time wall clock.** On the live install/update path, `now = DateTimeOffset.UtcNow` (Unix seconds) is the honest semantics of expiry: is this signature valid *now*. `notBefore <= now <= notAfter` (with 0 meaning unbounded) or → **INT011** (expired / not-yet-valid).
2. **Anti-rollback — persisted high-water time in the trust store.** Extend `TrustState` (`TrustState.cs`) with `highWaterUnixTime` (monotonic, advanced to `max(current, now)` on every *verified* apply, alongside the existing epoch advance, `TrustStateStore.cs:71-111`). The window check uses `effectiveNow = max(wallClockNow, highWaterUnixTime)`. So once the machine has verifiably seen time T, a signature whose `notAfter < T` stays expired even if the OS clock is wound back — the store is the ratchet. (This shares the C16 dormancy caveat: the high-water only advances when the store can be written, i.e. elevated; until C16 activation the anti-rollback half is best-effort and the wall-clock primary check still applies. Document honestly, as C14/C16 do.)
3. **Offline / reproducible / inspection verification** (`forge extract`, `forge verify --rebuild`, decompiler) has no meaningful "now" and must stay deterministic: it uses `effectiveNow = 0`-semantics **skipping** the window check (inspection-grade, as `BundleTrustVerifier` already documents empty-set inspection mode, `BundleTrustVerifier.cs:14-18`) OR a caller-supplied fixed reference time for a reproducible "as-of" verification. Expiry is a *runtime trust* property, not a *structural* one, so skipping it for structural inspection is correct.

`timestamp`-role signatures (§3.1) are the future path to a *trusted* "as-of" time independent of the OS clock (a countersignature attesting when the release was signed); this design defines the role and reserves the mechanism but the primary shipped enforcement is wall-clock + store high-water. Full timestamp-authority binding is a §10 follow-up.

### 8.4 Interaction with rotation

Validity windows make scheduled rotation cleaner: sign `K_old`'s final releases with a `notAfter` at the planned retirement date, so even engines that never revoke `K_old` stop honoring its signatures after that date — expiry as a *soft, pre-scheduled* revocation that needs no store write. Emergency revocation (hard, immediate) remains the `revoked[]` + epoch mechanism.

---

## 9. Implementation surface

Legend: **N** new, **M** modified. Every `.cs` edit → full-solution build (Gate 2); tests land in the same green commit as their implementation (Gate 1). Stages are independently shippable and each preserves the C14 default.

### Stage 1 — roles on trusted keys + M-of-N/threshold evaluator + baked default policy

Protocol / vocabulary:
- **N** `src/FalkForge.Engine.Protocol/Integrity/TrustRole.cs` — the `[Flags] enum` (§3.3).
- **N** `src/FalkForge.Engine.Protocol/Integrity/OperationKind.cs`, `RoleRequirement.cs`, `PolicyRule.cs` — the policy model (§5.1). (Contract cluster — may co-locate if <120 LOC.)
- **N** `src/FalkForge.Engine.Protocol/Integrity/TrustedSignature.cs` — the `(Fingerprint, Roles)` result record.
- **M** `IntegrityEnvelopeCodec.cs` — add `CollectTrustedSignatures` (§6.1); keep `MatchTrustedSignature` for revocation + the 1-of-any short-circuit.
- **N** `src/FalkForge.Engine.Protocol/Integrity/QuorumEvaluator.cs` — the distinct-key matching evaluator (§5.4).

Engine trust surface:
- **M** `src/FalkForge.Engine/TrustedKeys.targets` — emit `BakedTrustedKeys.Roles` (`FrozenDictionary<string,TrustRole>`) beside `Fingerprints`; parse `Roles=` metadata (§3.2). **N** emit `BakedTrustPolicy` (default = C14-equivalent, §7.1) from an optional `<FalkForgeTrustPolicy>` item.
- **M** (C18) `src/FalkForge.Engine/Integrity/EngineTrustAnchor.cs` — roles parameter on the three `Trust*` overloads (default `Release`); compute `EffectiveRoles` in the freeze union (§3.2); union roles on duplicate registration.
- **M** `src/FalkForge.Engine/Integrity/TrustPolicy.cs` — add `Roles` + `Rules` (§6.3).
- **M** `PayloadIntegrityGate.cs`, `SignedPayloadTocVerifier.cs` — operation resolution (§5.3) + collect-all + evaluate (§6.2); the trivial-rule short-circuit to preserve the C14 path.
- **M** `PipelineContext.cs` / `ApplyStep.cs` — thread the enriched `TrustPolicy` (Install operation).
- **M** `Program.cs`, `StagedUpdateVerifier.cs` — construct policies with the operation resolved per path.

New INT codes: **INT010** (policy/quorum unsatisfied).

Stage 1 tests (each encodes the *intent*, Gate 1):
- M-of-N accept exactly at threshold (2 distinct release keys satisfy `[(Release,2)]`).
- Reject one-below threshold (1 release key fails `[(Release,2)]` → INT010).
- Role-mismatch reject (a `developer`-only signature fails `Install=[(Release,1)]`).
- Role-AND reject (release-only fails `KeyChange=[(Release,1),(Recovery,1)]`; release+recovery passes).
- **Distinct-key enforcement:** one key holding `release|recovery` does NOT satisfy `[(Release,1),(Recovery,1)]` alone (needs two keys).
- Duplicate-signature dedup: same key twice counts once.
- **Backward-compat:** default (no roles, no policy) verifies an existing C14 bundle exactly as before; an existing v1 bundle still verifies.
- MSBuild: `Roles=` metadata lands in the generated `BakedTrustedKeys.Roles`.

### Stage 2 — signed, tighten-only, version-pinned bundle-carried policy + operation-resolution hardening

- **N** `src/FalkForge.Engine.Protocol/Integrity/SignedTrustPolicyEnvelope.cs` — the carried policy blob (policy table + `policyEpoch`), quorum-signed (reuses `ManifestSignatureEnvelope`/`CollectTrustedSignatures`).
- **N** `SignedTrustPolicyJsonContext` (AOT source-gen, mirrors `TrustStateJsonContext`).
- **M** `TrustState.cs`/`TrustStateStore.cs` — add `policyEpoch` (monotonic, §4.3); tighten-only merge `max(baked, carried)` per operation.
- **M** the gates — load carried policy (if present in the bundle), verify its quorum-signature against the pinned set, reject rollback (`policyEpoch < stored`) → **INT012**, then effective = `max(baked, carried)`.
- **M** `IntegrityBuilder.cs` / `IntegrityConfiguration.cs` / `EcdsaManifestSigner.cs` — author + quorum-sign a carried policy at build time.

New INT codes: **INT012** (carried policy invalid / rolled back / attempts to loosen).

Stage 2 tests:
- Carried policy tightens (raises Update to `[(Release,2)]`) and is enforced.
- Forged loosening carried policy is a no-op (effective stays at baked floor).
- Stripped carried policy falls back to baked floor (no fail-open).
- Rolled-back `policyEpoch` → INT012.
- Carried policy not quorum-signed → rejected, baked floor applies.

### Stage 3 — per-signature expiry windows

- **M** `SignatureEntry.cs` — `NotBefore`/`NotAfter` (§8.1); JSON context already covers the type.
- **M** `IntegrityEnvelopeCodec.cs` — per-entry windowed signed bytes with byte-identical neutral case (§8.2); window check in `CollectTrustedSignatures` (skip out-of-window entries).
- **M** `TrustState.cs`/`TrustStateStore.cs` — `highWaterUnixTime` monotonic advance (§8.3).
- **M** gates — pass `effectiveNow = max(wallClock, highWater)`; inspection paths skip the window (§8.3).
- **M** `IntegrityBuilder.cs`/`EcdsaManifestSigner.cs` — author validity windows per key.

New INT codes: **INT011** (signature expired / not-yet-valid).

Stage 3 tests:
- Expired signature (`notAfter < now`) → INT011; in-window → accept.
- Not-yet-valid (`notBefore > now`) → INT011.
- **Neutral-case byte-identity:** `0/0` window signed bytes are byte-identical to the C14 files(+epoch+revoked) bytes → existing bundles still verify (the compat hinge).
- Clock-rollback resistance: after high-water advance to T, a signature expiring before T stays expired even with the OS clock set back.
- Windowed tamper: extending `notAfter` breaks the signature (INT001).

### Existing tests to update
- `IntegrityEnvelopeCodecTests` — add collect-all cases beside the existing first-wins cases.
- `PayloadIntegrityGateTests`, `SignedPayloadTocVerifierTests` — accept the new policy/roles args; assert the trivial-rule default is unchanged.
- `EngineTrustAnchor` tests (C18) — roles on registration; `EffectiveRoles` union.
- `BakedTrustedKeysTests`, `TrustStateStoreTests` — roles emission, `policyEpoch`/`highWaterUnixTime`.
- Integration end-to-end (`BundleSigningEndToEndTests`) — a dual-signed bundle satisfying a `KeyChange` quorum end to end.

Docs: extend `docs/provenance.md` and the C14 design doc's error table with INT010/011/012; add a roles+policy section to the manual.

---

## 10. Non-goals / future

- **Publisher identity ↔ key binding (#1).** Roles say *what a key may do*, not *who owns it*. Binding a key to a verified publisher identity (a CA-issued cert, a Sigstore/Fulcio OIDC identity) layers on top: the `EffectiveRoles` map would key off an *identity* attested by a higher authority rather than a bare baked fingerprint. This design's role vocabulary is the natural attachment point for that later.
- **Transparency log (#17).** A tamper-evident append-only log of every signature/policy would let clients detect a split-view attack (a quorum-signed malicious bundle served to one victim). Orthogonal to quorum; would consume the same `TrustedSignature`/`policyEpoch` records as log entries.
- **Engine runtime self-attestation (#9).** Proving the *running engine binary* is the pinned one (measured boot / attestation) closes the "attacker ships their own engine build" gap C14 §2.2 assumes away. Quorum + roles assume a trusted engine; self-attestation would be what earns that assumption.
- **Timestamp-authority binding.** §8.3 defines the `timestamp` role and reserves the countersignature mechanism, but ships wall-clock + store-high-water enforcement. A true RFC-3161-style timestamp authority (offline-verifiable "as-of" time) is the follow-up that makes expiry fully clock-independent.
- **OTA delivery of new trusted keys / looser policy.** As in C14 §10, adding trust or relaxing policy without an engine rebuild is deferred; this design only ever *tightens* from outside (carried policy) and *removes* trust (revocation), never widens it OTA — the security-critical directions.
