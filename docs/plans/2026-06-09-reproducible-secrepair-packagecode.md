# Reproducible() × SECREPAIR — PackageCode Uniqueness Fix — Plan (2026-06-09)

Status: IMPLEMENTED (commits 645a6a4, 8d9d5d8, af0de14 + fix pass). Owner: TBD.
GitHub issue: https://github.com/Falkesand/FalkForge/issues/1
Goal: Make `Reproducible()` emit a unique PackageCode for non-identical packages so secure repair (SECREPAIR, KB2918614) no longer fails, while preserving byte-identical output for identical inputs.

## 1. Problem

With `Reproducible()` enabled, two builds of the same product/version with different payload content produce MSIs that share both ProductCode and PackageCode but differ in bytes. Windows Installer requires that non-identical packages carry unique package codes. On systems with secure repair active, Windows caches the source package hash at install time and re-validates it during repair; a repair sourced from a later same-version build fails the hash check. Downstream consumer plugwarden has disabled `Reproducible()` because of this ("SECREPAIR hash breakage, blocked on FalkForge fix").

## 2. Root Cause

1. `src/FalkForge.Core/Builders/PackageBuilder.cs:448-452` — reproducible mode derives ProductCode deterministically from identity only (`Name::Manufacturer::Version`), with no content input.
2. `src/FalkForge.Compiler.Msi/Recipe/MsiRecipeBuilder.cs:255` — PackageCode (SummaryInformation PID_REVNUMBER, PID 9) is unconditionally set to ProductCode:

```csharp
RevisionNumber = pkg.ProductCode.ToString("B").ToUpperInvariant(),
```

Same identity → same ProductCode → same PackageCode, regardless of content.

Secondary defect: an explicitly set ProductCode in normal (non-reproducible) mode freezes PackageCode across builds via the same line, exposing the same hazard.

## 3. Reproduction Spec

### Manual (one-time confirmation on a Windows box)

1. Build MSI v1.0.0 with `Reproducible()` and a payload file containing "A". Install it.
2. Change payload content to "B" (same version), rebuild with `Reproducible()`.
3. Run `msiexec /fvomus <newbuild>.msi /l*v repair.log`.
4. Expected failure today: package hash validation error in the log (secure repair) and/or stale cached package used. Capture the log excerpt into the GitHub issue.

### Automated (unit-level, drives the TDD cycle)

No Windows Installer service needed: assert directly on the compiled MSI's SummaryInformation PID 9. Two compilations with identical inputs must yield identical PID 9; two compilations differing only in payload bytes must yield different PID 9.

## 4. Architecture Decision — PackageCode Derivation

| Option | Pro | Con |
|--------|-----|-----|
| A. Content-digest UUID v5 (reproducible mode) | Deterministic for identical inputs AND unique per distinct content; satisfies both reproducibility and MSI uniqueness rule | Needs file bytes at derivation time → must run in compiler, not Core builder |
| B. Always `Guid.NewGuid()` for PackageCode | Trivially spec-correct | Breaks reproducibility — same inputs no longer yield identical bytes |
| C. Document limitation, no code change | Zero effort | plugwarden stays blocked; spec violation remains |

Pick **A** for reproducible mode, plus a slice of **B** for normal mode (fresh `Guid.NewGuid()` even when ProductCode is explicit).

Derivation (reproducible mode): `PackageCode = GuidUtility.CreateDeterministicGuid(FalkForgeNamespace, digest)` where `digest` = SHA-256 over the sorted sequence of `(targetPath, fileSha256)` pairs for all resolved payload files, concatenated with ProductCode and Version. Computed in the Compiler.Msi layer where resolved file content is available (Core's `PackageBuilder` never sees file bytes).

Note: `SummaryInfoPatcher` does not touch PID 9, so no patcher change needed. ICE validation is already skipped in reproducible mode (`MsiAuthoring.cs:316`) — unaffected.

## 5. Files Affected

### Compiler.Msi
- `src/FalkForge.Compiler.Msi/Recipe/MsiRecipeBuilder.cs` — replace `RevisionNumber = pkg.ProductCode...` with new derivation (content digest in reproducible mode, fresh GUID otherwise).
- Possibly a small `PackageCodeDerivation` helper (new file) if the digest logic exceeds a few lines.

### Core
- `src/FalkForge.Core/GuidUtility.cs` — reuse as-is (no change expected).

### Tests
- `tests/FalkForge.Compiler.Msi.Tests/` — new PackageCode derivation tests (see §6).

## 6. TDD Spec — Failing-Test Order

| # | Test | Purpose / Asserts |
|---|------|-------------------|
| 1 | `Reproducible_SameInputs_SamePackageCode` | Two compilations, identical inputs → identical PID 9. Guards reproducibility through the fix. |
| 2 | `Reproducible_DifferentPayloadBytes_DifferentPackageCode` | Same identity/version, payload byte change → different PID 9. RED today (both equal ProductCode). |
| 3 | `Reproducible_PackageCodeDiffersFromProductCode` | PID 9 must not equal ProductCode in reproducible mode (content digest, not identity). RED today. |
| 4 | `NormalMode_ExplicitProductCode_FreshPackageCodePerBuild` | Explicit ProductCode, two builds → different PID 9. RED today (secondary defect). |
| 5 | Existing reproducibility byte-identity test(s) | Must stay GREEN — same inputs still produce byte-identical MSI. |

## 7. Open Questions

| # | Question | Status |
|---|----------|--------|
| 1 | Does anything else key off PackageCode == ProductCode (decompiler round-trip, patch/MSP baseline validation)? | Verified: PatchCompiler uses `patch.Id`, MsmCompiler uses `module.Id`, Decompiler `GetSummaryProperty` is a stub — no PackageCode==ProductCode dependency. **RESOLVED.** |
| 2 | Should the digest include non-file model content (registry, properties) so any model change refreshes PackageCode? | Decided: files + identity + epoch digest only. Registry/property changes do not affect SECREPAIR validation of the byte stream; full-recipe digest would be circular via RecipeContentHasher. InstanceId nonce covers normal-mode uniqueness. **RESOLVED.** |
| 3 | Manual SECREPAIR repro on real Windows before or after fix? | Before, to attach log evidence to issue #1. |

## 8. Out of Scope

- Bundle-layer reproducibility (`BundleBuilder.Reproducible()`) — bundles have no PackageCode; unaffected.
- MsiFileHash table emission (separate feature, not required for this fix).
- Re-enabling ICE validation in reproducible mode.
