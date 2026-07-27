# CONTEXT — Domain Language

The ubiquitous language for FalkForge: the terms the code, tests, commits, and conversations
should all use the same way. This is filled in from the real domain, not from generic software
words. Keep it short and current — a stale glossary is worse than none. When a term's meaning
changes, update it here in the same commit.

> How to use: an agent reads this before writing or reviewing code so that names match the domain
> and boundaries are respected. If a code change introduces a new domain term or shifts an
> existing one, add/update the entry here.

## Purpose

FalkForge is a C# framework for authoring and compiling Windows installers — MSI, MSM, MSP, MST,
and self-extracting EXE bundles — via a fluent API and a NativeAOT install-time engine, with no
external tools (no WiX toolset, no `msi.dll` shipped separately). Its users are .NET developers
who need a programmable, testable installer build (fluent authoring, `Result<T>` error handling,
reproducible builds) instead of hand-written WiX XML, plus end users who run the compiled
installer/bundle at install time.

## Bounded contexts / modules

| Context | Responsibility | Owns (data/decisions) | Talks to |
|---------|----------------|------------------------|----------|
| `FalkForge.Core` | Domain model + fluent authoring API (`Installer.Build()`/`.BuildBundle()`/...) | `PackageModel`, `IntegrityConfiguration`, signing provider contracts, `Result<T>`/`Error` | Consumed by both compilers, Engine.Protocol, extensions |
| `FalkForge.Compiler.Msi` | Turns a `PackageModel` + attached extensions into a compiled MSI via `msi.dll` P/Invoke | The **recipe** pipeline (producers → rows → tables), ICE validation, dialog templates | `FalkForge.Extensibility` (contributors), `Platform.Windows` |
| `FalkForge.Compiler.Bundle` | Builds the self-extracting EXE bundle: packages, chain, delta compression, integrity signing | `BundleModel`, TOC layout, delta/basis reconciliation at build time | `Engine.Protocol` (shared bundle/integrity types) |
| `FalkForge.Engine` | NativeAOT install-time runtime (the process that actually runs on the end user's machine) | The installer pipeline (`InstallerPipeline`/`IPhaseStep`), trust gates, elevation client | `Engine.Protocol` (wire format), `Engine.Elevation` (via named pipe) |
| `FalkForge.Engine.Elevation` | NativeAOT elevated companion process — the only code in the system that runs as SYSTEM/Administrator on behalf of the Engine | Elevated command execution, trust-state advance under the restrictive store ACL | `Engine.Protocol`, invoked only via the elevation gateway |
| `FalkForge.Engine.Protocol` | AOT-safe IPC message types, wire codecs, and the integrity/trust model shared by Engine, Elevation, and the UI | `TocEntry`, `InstallerManifest`, integrity envelope codec, trust state/quorum/policy types | Referenced by Engine, Engine.Elevation, Ui.Abstractions, Compiler.Bundle |
| `FalkForge.Extensibility` | The extension contract surface (contributors, registry, compatibility checks) | `IExtensionRegistry`, `IFalkForgeExtension`, `ExtensionRegistration` | Implemented by every `Extensions.*` project, consumed by the compilers |
| `Extensions.*` (Firewall/Iis/Sql/DotNet/Dependency/Util/Driver/Http) | First-party extensions that contribute MSI tables, components, and elevated execution steps for a specific OS feature | Their own table/component/execution contributors | `FalkForge.Extensibility`, attached via `compiler.Use(extension)` |
| `FalkForge.Cli` | Spectre.Console CLI: build, validate, inspect, decompile, verify, plan-diff | Signing config resolution, CLI-side verification (`MsiIntegrityVerifier`) | Both compilers, `Decompiler`, `Signing.SignServer` |

## Glossary

<!-- One row per term. A term used in code MUST mean the same thing here. -->

| Term | Definition | Notes / invariants |
|------|------------|---------------------|
| **Bundle** | A self-extracting EXE that carries one or more MSI **packages** plus their **payloads**, a **TOC**, and a signed **manifest**. Modeled at build time by `BundleModel` (`FalkForge.Compiler.Bundle/BundleModel.cs`) and read at install time via `BundleReader`/`Engine.Protocol.Bundle`. | Distinct from an MSI package: a bundle chains/installs one or more packages plus prerequisites. |
| **Payload** | A single file embedded in a bundle's container (an MSI, an MSP, a companion exe, or an arbitrary file) and described by exactly one **TOC entry**. | Extracted, hash-verified against its TOC entry before it ever lands on disk (see `BundleReader`). |
| **TOC** (table of contents) | The bundle's index of payloads: `TocEntry` (`FalkForge.Engine.Protocol/Bundle/TocEntry.cs`) records each payload's `PackageId`, offset, sizes, SHA-256 hash, and — for a **delta** payload — its `BaseSha256Hash`/`ReconstructedSha256Hash`. | The TOC hash is what the **integrity envelope** actually signs; declared metadata (e.g. the companion's hash) must match the TOC, which must match the bytes — "bytes == TOC == declared (== signed)" (`BootstrapCompanionResolver.cs`). |
| **Manifest** | `InstallerManifest` (`FalkForge.Engine.Protocol/Manifest/`) — the signed description of a bundle's contents: package list, chain order, external containers, companion hash, trust metadata. What the **integrity envelope** signature actually covers. | One manifest per bundle; signed via the hybrid ECDSA-P256 + ML-DSA-65 scheme (see ADR-0005). |
| **Recipe** | The MSI compiler's internal build plan: an ordered set of **producers** whose rows are assembled into MSI tables before the `msi.dll` write. `MsiRecipeBuilder`/`MsiRecipeExecutor`/`RecipeBuildContext` (`FalkForge.Compiler.Msi/Recipe/`). | Not exposed outside `Compiler.Msi`; the fluent `PackageModel` is the recipe's input, the compiled MSI is its output. |
| **Producer** | An internal (`ITableProducer`, `FalkForge.Compiler.Msi/Recipe/ITableProducer.cs`), built-in generator of rows for one MSI table from the `RecipeBuildContext`. First-party and fixed — not an extension point. | Contrast with **Contributor** below: a Producer is how `Compiler.Msi` builds its *own* tables; a Contributor is how an *extension* adds tables the compiler doesn't know about natively. |
| **Contributor** | An extension-supplied hook (`IMsiTableContributor`, `IComponentContributor`, `IExecutionContributor`, `IDryRunContributor`, `IDialogStepBuilder` — `FalkForge.Extensibility/`) registered via `IExtensionRegistry` and invoked by the compiler pipeline to add custom tables, components, elevated execution steps, or dialog steps. | Registration is explicit and compile-time only (ADR-0004) — there is no scanning for contributors. |
| **Trust anchor** | The baked-in root of trust the Engine verifies bundle signatures against — `EngineTrustAnchor` (`FalkForge.Engine/Integrity/EngineTrustAnchor.cs`) plus the trusted-key set embedded via `TrustedKeys.targets`. | Distinct from the *dynamic* trust state (below): the anchor is fixed at Engine build time; the trust state is a mutable, monotonic record on the end user's machine. |
| **Quorum** | The rule for how many of a bundle's attached signatures must independently verify before a bundle/manifest is accepted — evaluated by `QuorumEvaluator` (`FalkForge.Engine.Protocol/Integrity/QuorumEvaluator.cs`) against `BakedTrustPolicy`/`PolicyRule`. | Exists so multi-signer setups (e.g. rotation, hybrid classical+PQ) have an explicit, testable "how many signatures are enough" rule rather than an implicit all-or-nothing check. |
| **Epoch** | A monotonically-increasing key-generation counter carried in the manifest's `ManifestSignatureEnvelope.Epoch` and mirrored in the on-disk `TrustState.Epoch`. The publisher bumps it only when a key is retired/revoked, not per release. | A client refuses any bundle whose epoch is below the highest it has ever accepted (INT008) — this is the anti-downgrade/anti-replay mechanism. See `TrustState.cs`, `BakedTrustPolicy.cs`. |
| **Revocation** | A publisher key fingerprint explicitly marked untrusted; carried in the manifest's revocation list and persisted in `TrustState.RevokedFingerprints`. | A revoked key is *skipped*, not fatal, during verification — so a bundle validly signed by a still-trusted key still passes even if another attached signature is by a revoked key (`IntegrityEnvelopeCodec.cs`). |
| **Delta / basis** | A **delta** bundle (`DeltaBundleCompiler`, `DeltaCompressor`) carries only the Octodiff-computed byte differences between a new payload and its **basis** — the old (previous-version) payload it was diffed against. `DeltaApplicator` (`FalkForge.Engine.Protocol/Bundle/DeltaApplicator.cs`) reconstructs the full payload at apply time, first proving the supplied basis's hash matches the delta's declared `BaseSha256Hash`. | Reconstruction is verified end-to-end: basis hash checked before applying, reconstructed output hash (`ReconstructedSha256Hash`) checked after. A wrong/tampered basis is rejected before any bytes are trusted. |
| **Companion** | The `FalkForge.Engine.Elevation` executable, embedded as a payload inside the bundle under a reserved TOC id, extracted alongside the rest and wired for elevated execution only once its TOC hash matches the manifest's declared `EngineCompanionSha256` (`BootstrapCompanionResolver.cs`). | Fail-closed: a manifest that declares no companion never wires one, even if a payload happens to occupy the reserved id — an undeclared SYSTEM-capable binary must never run. |
| **Elevation gateway** | The named-pipe transport (`NamedPipeElevationGateway`, `IElevatedCommandGateway`, `FalkForge.Engine/Pipeline/`) the (unprivileged) Engine process uses to hand elevation-requiring commands to the (privileged) Elevation companion. | The only path by which the Engine process causes privileged work to happen; mutual authentication over the pipe is part of the trust chain (see the 2026-07-10 defensive-review record). |
| **Sigil** | A third-party, user-installed code-signing CLI tool (invoked via PATH, like `git`/`dotnet`) that MSI/Bundle compilers shell out to for Authenticode signing. Detected by `SigilDetector` (`FalkForge.Core/Signing/SigilDetector.cs`), invoked by `SigilProcessRunner`/`SigilSigner`/`BundleSigilSigner`. | Not a FalkForge-authored cryptographic primitive — an external signing tool integration, separate from the in-process ECDSA/ML-DSA integrity-envelope signing. |
| **Integrity envelope** | The signed wrapper around a manifest/TOC's canonical bytes — `IntegrityEnvelopeCodec`/`ManifestSignatureEnvelope` (`FalkForge.Engine.Protocol/Integrity/`) — that binds the file list, epoch, revocation list, and external-container set into one signed message so none of them can be stripped or altered independently of the signature. | The hybrid signing scheme (ADR-0005) produces one envelope carrying both the ECDSA-P256 and ML-DSA-65 signatures. |

## Domain primitives

Strongly-typed values that replace raw strings/ints at boundaries (per `rules/security.md`).

| Primitive | Underlying | Valid when |
|-----------|------------|------------|
| `PackageCode` (`PackageModel.PackageCode`) | `Guid?` | Fresh GUID per normal build; `null` for a reproducible build, in which case the compiler derives a UUID v5 content digest via `PackageCodeDerivation` so non-identical packages never collide (issue #1/SECREPAIR). |
| `TocEntry.Sha256Hash` / `BaseSha256Hash` / `ReconstructedSha256Hash` | `string` (hex) | Only ever compared, never trusted as a label — every payload extraction and delta reconstruction re-verifies bytes against the declared hash before use. |
| `ManifestSignatureEnvelope.Epoch` | `int` | Monotonic per machine (`TrustState.Epoch`); a client-observed epoch may only move forward, never backward. |
| `SessionCorrelationId` (`IFalkLogger`) | `Guid` | Set once per session before any log calls; stamped on every `LogEntry` and forwarded across process boundaries in `LogMessage` frames so UI/Engine/Elevation log streams can be correlated. |

## Key rules & invariants

- A bundle's TOC hash, the manifest's declared hash for any TOC-referenced item (e.g. the
  companion), and the actual extracted bytes must all agree before that item is trusted — "bytes
  == TOC == declared (== signed, when present)" (`BootstrapCompanionResolver.cs`).
- A client's accepted trust `Epoch` only ever advances; a bundle signed at a lower epoch than the
  client has already seen is rejected as a replay (INT008), before any other trust check runs.
  See ADR-0005 and `TrustState.cs`.
  - `TrustState` only ever advances through the elevated **companion** (`TrustStateAdvance`
    command), because the on-disk trust-state file's restrictive ACL cannot be written by an
    unprivileged process — a per-user (non-elevating) install run simply does not advance the
    store, and enforcement is correct only for the elevated path.
- Extensions attach only via explicit, compile-time registration (`compiler.Use(extension)`); there
  is no assembly-scanning or directory-drop discovery (ADR-0004).
- `Engine`, `Engine.Elevation`, and `Engine.Protocol` contain no reflection, no `dynamic`, no
  `BinaryFormatter`, and no general-purpose DI container — composition is manual and JSON is
  source-generated only (ADR-0002).
- A **delta** payload is never trusted on its own: the **basis** hash is checked before
  reconstruction and the reconstructed payload's hash is checked after, so a wrong or tampered
  basis bundle cannot silently produce a corrupted install.

## Out of scope / explicit non-goals

- FalkForge does not ship or bundle a copy of `msi.dll`, WiX, or the `sigil` signing tool — it
  P/Invokes/shells out to tools the environment already provides.
- FalkForge does not implement its own certificate authority or code-signing PKI — Authenticode
  signing is delegated to `sigil` or `FalkForge.Signing.SignServer` (Keyfactor SignServer),
  never a home-grown CA.
- FalkForge does not provide generic application hosting/dependency-injection infrastructure for
  consumers — see ADR-0002; it is an installer-authoring and install-time-execution framework,
  not an application framework.
- MSIX support (`FalkForge.Compiler.Msix`) is explicitly experimental and not CLI-dispatched; it
  is not part of the supported surface this glossary describes as stable.
