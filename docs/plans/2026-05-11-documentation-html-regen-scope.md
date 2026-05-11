# documentation.html — Regen Scoping (2026-05-11)

Status: SCOPING. No edits to `documentation.html` yet.
Owner: TBD.
Branch: `main` at `6496e61`.

## Context

`documentation.html` (487 KB, 9159 lines) at repo root is the user-facing FalkForge documentation. The last full content regen was 2026-05-05. Since then, the 2026-05-10 stability sweep landed 21 commits and deepening RFC cycles 2–6 reshaped the engine, the extensibility layer, the dialog framework, the protocol codec, and the CLI surface. The doc is now content-stale, even though as of `1b48b31` the `docs/gen/` fragments are byte-aligned with the rendered HTML.

This document is the scoping output for a future regen session — a concrete punch list of sections to rewrite, expand, patch, or add.

## Section impact summary

| Section | Lines | Action | Effort |
|---|---|---|---|
| S3 "Solution Structure" | 945–1046 | rewrite | M |
| S3 sub "Dependency Graph" | 1013–1045 | patch | S |
| S11 "Engine Architecture" | 5676–6214 | rewrite (largest) | L |
| S12 "Protocol & IPC" | 6219–6389 | expand | S |
| S13 "Dialog Customization" | TBD | add subsection | S |
| S14 "CLI Tool" | 6515–6658 | expand | M |
| S16 "Decompiler" | 6764–6832 | expand | M |
| S19 "Studio" | 7956+ | confirm + add experimental caveat for MSIX | S |
| New: InstanceLock subsection (in S11) | — | add | S |
| New: WinGet manifest subsection (in S7/S8) | — | add | S |
| New: Delta updates subsection (in S11 + Bundle) | — | add | S |
| New: MSIX experimental compiler (S3 + S19) | — | add stub | S |
| New: Plugin system API reference | — | add new section | M |
| Extension validation unification | — | patch S(extensibility) | S |

Priority order for actual rewrite session: S11 (L, blocks everything) → S14 (M) → S3 (M) → S16 (M) → S12 (S) → plugin section (M) → all S patches.

## Section-by-section drift

### S3 "Solution Structure" (lines 945–1046) — rewrite

- Current claim: "21 source projects, 21 test projects". `EngineStateMachine`, `EngineHost`, `EngineContext` listed as live types. No Pipeline subdir. No `Compiler.Msix`. No `Extensions.Dependency`.
- Reality (per CLAUDE.md): 26 src + 22 test projects. `EngineStateMachine` + `EngineHost` + `EngineContext` deleted (commits `7a90ba2`, `0e43db4`, `43d70db`). Replaced by `IInstallerPipeline` / `PipelineRunner` / `EngineSession`. `Compiler.Msix` added (experimental). `Extensions.Dependency` exists.

### S3 sub "Dependency Graph" (lines 1013–1045) — patch

- Current: `Decompiler = Core + Compiler.Msi` only; no `Compiler.Msix` branch; `Cli` missing `Extensibility` + extension deps; `Engine` exe = old composition.
- Reality (per CLAUDE.md): `Decompiler` also refs `Compiler.Bundle`. `Engine` exe now = `Pipeline + Protocol + Platform.Windows` (no `Compiler.Msi`).

### S11 "Engine Architecture" (lines 5676–6214) — rewrite (largest single change)

- Current claims: `EngineHost` ctor, `EngineContext` fields table, `EngineStateMachine` with phase handlers, `HandleUiMessageAsync()`, property flow through `EngineHost → EngineContext.UserProperties`.
- Reality: `EngineHost` deleted (`0e43db4`); `EngineStateMachine` deleted (`43d70db`); `EngineContext` deleted (`43d70db`). New architecture:
  - `EngineSession` facade (`b5b536f`)
  - `IInstallerPipeline` + `InstallerPipeline` (`60cb6a1`)
  - `PipelineRunner` event loop (`ab1d98d`)
  - `IUiChannel` (`NamedPipeUiChannel`) and `IElevatedCommandGateway` (`NamedPipeElevationGateway`)
  - Concrete phase steps: `DetectStep`, `PlanStep`, `ApplyStep`, `ElevateStep`, `RollbackStep`
- Also missing from current doc:
  - `InstanceLock` (`fae691c`) — per-bundle named-semaphore concurrency guard
  - `--log` / `--log-level` CLI flags (`5b0850d`)
  - Log rotation + metrics infrastructure (`89862f3`, `91d6b4c`)
  - `SessionCorrelationId` cross-process (`fceb974`)

### S12 "Protocol & IPC" (lines 6219–6389) — expand

- Current claim: wire format `[Version][Type][Length][SequenceId][Payload]`. `LegacyMessageSerializer` implied (codec-facade title hints at it). No `SessionCorrelationId`. No codec-facade doc.
- Reality: `LegacyMessageSerializer` deleted (`3d24467`); parity tests converted to golden-byte tests. `SessionCorrelationId` added (`fceb974`). Wire format likely updated. "Codec facade" framing from RFC Cycle 5 missing.

### S13 "Dialog Customization" — add subsection (RFC Cycle 6)

- Current: S13 covers built-in templates only.
- Reality: `IDialogStepBuilder`, `DialogBuildContext`, `DialogStepRegistry`, `InsertedDialogStep` added (RFC Cycle 6, `dcabf3b`, `c4a5a2e`). Custom dialog-injection API entirely absent.

### S14 "CLI Tool" (lines 6515–6658) — expand

- Current: `build` / `validate` / `inspect` / `decompile` / `bundle detach|reattach` only. No `--dry-run`, no `--json`, no `forge winget`, no `forge extract`, no `forge rules list|explain`, no `forge plan`.
- Reality: `--dry-run` (`43762ca`), `--json` flag (`aa56c86`), `forge winget` (per CLAUDE.md), `forge extract` (per CLAUDE.md), `forge rules list|explain` + validate `--ignore` / `--warn-as-error` / `--stop-on-first-error` (`b81258e`). `PlanCommand` hidden but exists (`03760fb`).

### S16 "Decompiler" (lines 6764–6832) — expand

- Current: `MsiDecompiler`, `IMsiTableAccess`, `CSharpEmitter`, 9 `TableReaders`. No bundle decompiler. No WiX Burn decompiler.
- Reality: `BundleDecompiler`, `WixBurnAccess`, `WixBundleDecompiler` exist. Also missing: `ITableQuery` (`2bfc4f4`), `IMsiTableContributor.ReadSchema` (`c5d86a6`), `MsiReadRecipe` + `DecompileToRecipe` (`67fbb66`) — the new round-trip API. Extension `ReadSchema` for SQL (`40b83bb`) and Firewall (`2ba746f`) round-trips.

### S19 "Studio" (lines 7956+) — confirm with caveat

- Current: 22 editors, MSIX project type supported.
- Reality: Studio section appears accurate structurally; MSIX is marked `[Experimental]` in code (`FF_MSIX001`) but doc presents it as a first-class feature. Needs experimental caveat.

### Extension validation unification — patch S(extensibility)

- Current claim: `IExtensionValidator` per-extension.
- Reality: `IExtensionValidator` cluster deleted (`a91d675`). All extensions now use `GetValidationRules()` returning `ValidationRule[]` (`31767b5`), merged via the Phase 13–16 commits. `DLG001` / `DLG002` dialog validators added.

### Plugin system — add new section

- Current: mentioned only in S3 project list.
- Reality: `IInstallerPlugin`, `PluginRegistry` (`421f2d9`), `IPluginServiceRegistry` (first-registration-wins + Freeze), and the three shipped plugins (`Plugins.Sql`, `Plugins.Odbc`, `Plugins.FileSystem`) have no API-reference section anywhere in the doc.

## Architectural commits not yet reflected in documentation.html

| SHA | Subject | Target section |
|---|---|---|
| `b5b536f` | feat(engine): EngineSession facade + EngineOutcome | S11 (full rewrite) |
| `60cb6a1` | feat(engine.pipeline): IInstallerPipeline + InstallerPipeline + InstallerPipelineBuilder | S11 |
| `ab1d98d` | feat(engine): PipelineRunner event-loop | S11 |
| `0e43db4` | refactor(engine): delete EngineHost + 6 phase handlers | S11 (removes documented types) |
| `43d70db` | refactor(engine): delete EngineContext + EngineStateMachine | S11 (removes documented types) |
| `fae691c` | fix(engine): InstanceLock named-semaphore | S11 (new subsection) |
| `3d24467` | refactor(engine.protocol): delete legacy serializer + golden-byte parity tests | S12 |
| `fceb974` | feat(engine,protocol): SessionCorrelationId | S12 |
| `5b0850d` | feat(engine): --log / --log-level CLI flags | S11 (logging subsection) |
| `89862f3` | feat(engine/logging): metrics + log rotation infrastructure | S11 |
| `91d6b4c` | feat(engine): metrics emission across pipeline | S11 |
| `43762ca` | feat(cli): --dry-run flag for forge build | S14 |
| `aa56c86` | feat(cli): --json flag for forge build/validate/inspect/plan | S14 |
| `b81258e` | feat: forge rules list/explain + validate flags | S14 |
| `67fbb66` | feat(decompiler): MsiReadRecipe + DecompileToRecipe | S16 |
| `2bfc4f4` | feat(extensibility): ITableQuery | S16 (table reader API) |
| `c5d86a6` | feat(extensibility): ITableReadSchema + IMsiTableContributor.ReadSchema | S16 |
| `40b83bb` | feat(sql): ReadSchema round-trip | S16 |
| `2ba746f` | feat(firewall): ReadSchema round-trip | S16 |
| `a91d675` | refactor(extensibility): delete IExtensionValidator cluster | S(extensibility) |
| `31767b5` | feat(extensibility): extension rule merge GetValidationRules() | S(extensibility) |
| `dcabf3b` / `c4a5a2e` | feat(compiler.msi): RFC Cycle 6 dialog customization API | S13 |
| `421f2d9` | feat(core/plugins): PluginRegistry bulk composition helper | S(plugins, new) |
| `d8806f3` | chore: flag MSIX experimental | S3 + S19 |
| `fb8b863` | fix(ui): NativeAOT-safe Func<Window> factory | S11 / UI section |

## Workflow notes

- `docs/gen/` is gitignored; fragments are local-only. Per `664b8ac`, none are tracked.
- Re-slice from `1b48b31` produced 7 contiguous fragments: `header.html` + `section1.html` + `section2a.html` + `section2b.html` + `section3.html` + `section4.html` + `footer.html`. SHA-256 of the concatenation matches `documentation.html` exactly.
- Recommended regen workflow: edit `documentation.html` in place (it's the source of truth), then re-slice fragments via the same script the 2026-05-11 session used. Or: pick fragment-first and concat back.

## Effort estimate (rough)

- L: S11 — multi-day rewrite. Touches three architectural layers (Session, Pipeline, Protocol). Should be done after the regen rewrite plan is reviewed.
- M: S3, S14, S16, plus the new Plugin section. ~half day each.
- S: S12, S13 subsection, S19 caveat, all "add subsection" items, all "patch" items. Hours each.

Total for full regen: ~3–5 focused days. Could be split across multiple sessions, one section at a time.
