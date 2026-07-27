# 5. Betting on the preview ML-DSA / post-quantum signing surface

- Status: Accepted
- Date: 2026-07-27
- Deciders: Peter Falkesand

## Context

FalkForge signs bundle and manifest integrity envelopes with a hybrid scheme: classical
ECDSA-P256 (`SignatureAlgorithms.EcdsaP256`, `src/FalkForge.Core/Signing/SignatureAlgorithms.cs:16`)
combined with ML-DSA-65, the FIPS 204 post-quantum signature algorithm exposed by .NET 10's
still-`[Experimental]` `System.Security.Cryptography.MLDsa` API
(`src/FalkForge.Core/Signing/EphemeralMLDsaSignatureProvider.cs`,
`src/FalkForge.Core/Signing/MLDsaPemSignatureProvider.cs`). Three central assemblies suppress the
experimental-API diagnostic to use it: `FalkForge.Core`, `FalkForge.Compiler.Bundle`, and
`FalkForge.Engine.Protocol` each carry `<NoWarn>$(NoWarn);SYSLIB5006</NoWarn>`, with the rationale
recorded directly in each csproj (`FalkForge.Core.csproj:6-11`):

> Deliberate opt-in to the .NET 10 ML-DSA (FIPS 204) API for PQ-hybrid manifest signing
> (`docs/plans/2026-07-10-pq-hybrid-signature-design.md`). SYSLIB5006 marks the API surface
> [Experimental]; the shipped CNG-backed implementation works (probe-verified, incl. NativeAOT).
> Risk is API-shape churn on .NET upgrades, not crypto correctness — and the hybrid model fails
> closed: a PQ bug can only reject, never accept alone.

`FalkForge.Engine.Protocol`'s copy of the same comment adds "AOT-safe (probe-verified NativeAOT)"
— consistent with ADR-0002's NativeAOT constraint on that assembly. The signature is verified via
`QuorumEvaluator`/`BundleTrustVerifier`/`BakedTrustPolicy` such that the classical and PQ
signatures are both checked, and — per the csproj comment and the hybrid design intent — a defect
in the PQ verifier can only cause a spuriously-rejected (safe) verification, not a
spuriously-accepted (unsafe) one; the classical ECDSA check still gates acceptance independently.

## Decision

We will use .NET 10's `[Experimental("SYSLIB5006")]` `MLDsa` API for the ML-DSA-65 leg of hybrid
bundle/manifest signing in `FalkForge.Core`, `FalkForge.Compiler.Bundle`, and
`FalkForge.Engine.Protocol`, suppressing SYSLIB5006 in those three csproj files rather than
avoiding the experimental surface or vendoring a third-party PQ implementation. The hybrid design
requires PQ verification to fail closed only (reject-only failure mode) so that API instability in
the experimental surface cannot itself become an authentication bypass.

## Consequences

- **Three central assemblies are tied to a preview API that can change shape between .NET
  releases.** `FalkForge.Core`, `FalkForge.Compiler.Bundle`, and `FalkForge.Engine.Protocol` sit on
  the critical path of every bundle build and every install-time trust check — an `MLDsa` API
  surface change in a future .NET version (parameter types, method names, or removal of the
  experimental gate entirely) forces a coordinated update across all three, plus the NativeAOT
  probes for `Engine.Protocol`.
  - Mitigated in scope, not eliminated: because the failure mode is reject-only, an upgrade
    that silently breaks the PQ verifier degrades trust checks toward "PQ leg always fails" (safe,
    if noisy) rather than toward silent bypass — but a build-time signer failure would still block
    releases until fixed.
- **NoWarn suppression must be re-justified, not silently carried forward.** Each of the three
  csproj comments exists specifically so a future contributor does not treat `SYSLIB5006` as
  routine noise; removing the suppression (e.g. once the API stabilizes and the attribute is
  dropped upstream) is a signal to revisit this ADR, not just delete a `NoWarn` line.
- **No parallel non-experimental PQ path exists.** If the experimental API is withdrawn or
  changed incompatibly before it stabilizes, there is currently no fallback PQ signer already built
  — the migration would have to happen under time pressure rather than at a chosen point.
- **Benefit accepted in exchange for the risk:** FalkForge ships hybrid PQ-resistant signing years
  before a stable BCL API would otherwise allow it, using a CNG-backed, NativeAOT-probe-verified
  implementation rather than a third-party or hand-rolled PQ primitive.
