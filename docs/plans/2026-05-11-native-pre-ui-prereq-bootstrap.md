# Native Pre-UI Prerequisite Bootstrap — Plan (2026-05-11)

Status: SCOPING. No code yet.
Owner: TBD.
Goal: VS/Office-style native bootstrap probe + install of runtime prereqs (e.g. .NET 10 Desktop) BEFORE engine spawns the managed WPF UI process.

## 1. Problem

`Program.RunAsBootstrapper` (`src/FalkForge.Engine/Program.cs` line 307) extracts payloads then `Process.Start`s the UI exe at line 413. Engine itself is NativeAOT (no .NET dep). UI is `net10.0-windows` + `UseWPF` — framework-dependent. On a host lacking .NET 10 Desktop Runtime, UI launch hard-fails (no managed code surface to catch). Current `.Prerequisite() / .SearchCondition() / .InstallCondition()` knobs run inside the pipeline `DetectStep/PlanStep/ApplyStep` — i.e. AFTER the UI is alive. Useless if the missing prereq IS .NET itself.

VS / Office model: tiny native bootstrapper detects runtime, installs if missing (TaskDialog with progress), then launches managed UI. Adopt that.

## 2. Architecture Decisions

### 2.1 Naming + slot — Decision: `PreUIPrerequisite`

Three candidates considered:

| Name | Pro | Con |
|------|-----|-----|
| `PreUIPrerequisite` | clear vs ordinary `.Prerequisite()`; aligns with phase | longer name |
| `BootstrapPrerequisite` | implies engine bootstrap phase | conflict with "bootstrapper" (whole .exe) |
| `NativePrerequisite` | hints at AOT-only constraint | leaks impl detail |

Pick **`PreUIPrerequisite`**. User-facing concept is "must be installed before UI shows". "Pre-UI" matches the engine phase distinction and reads in the fluent API.

### 2.2 Storage — Decision: separate `PreUIPackages` list

Two options:

- A. Flag on existing `BundlePackageModel` (e.g. `IsPreUI : bool`). Reuses chain plumbing. Risk: pre-UI prereqs accidentally re-detected/re-installed by `PackageDetector` during normal pipeline; couples lifecycle.
- B. New `IReadOnlyList<PreUIPackageModel> PreUIPackages` on `BundleModel`, with its own model type, manifest list, and TOC tier.

Pick **B**. Hard separation prevents pipeline from touching these packages. Engine's pre-UI code path has its own loader. Distinct manifest field = trivial back-compat (older engine ignoring unknown field is non-issue since engine + manifest version-locked per build).

### 2.3 Native UI — Decision: TaskDialogIndirect (comctl32)

Three options:

| Approach | LOC est. | Risk |
|---------|----------|------|
| TaskDialog (`comctl32.dll!TaskDialogIndirect`) | ~250 | Requires v6 common controls manifest; needs progress bar marquee/percent |
| Win32 HWND + CreateWindowEx + custom paint | ~600 | Full UI code; HiDPI handling |
| Console output | ~50 | Ugly; no progress bar; defeats the "VS-style" goal |

Pick **TaskDialog**. AOT-compat confirmed by existing `NativeRestartManagerMethods` pattern (`LibraryImport` / `DllImport` with `DefaultDllImportSearchPaths(System32)`). TaskDialog supports `TDF_SHOW_PROGRESS_BAR` (percent) and `TDF_SHOW_MARQUEE_PROGRESS_BAR` (indeterminate). Callback delivers progress via `TDM_SET_PROGRESS_BAR_POS`. Cancel button → `TDN_BUTTON_CLICKED`.

Requirement: engine `.exe` must ship a side-by-side comctl32 v6 application manifest (`<dependency><dependentAssembly>...Microsoft.Windows.Common-Controls...</dependentAssembly></dependency>`). Embed via `<ApplicationManifest>` in the `FalkForge.Engine.csproj` or via `linker.exe /MANIFEST:EMBED`. Verify on AOT-published binary.

### 2.4 Detection — Decision: reuse `SearchConditionEvaluator`, new minimal driver

`src/FalkForge.Engine/Detection/SearchConditionEvaluator.cs` already evaluates RegistryValue / FileExists / FileVersion / DirectoryExists against `IRegistry` + `IFileSystemProvider`. AOT-safe (no reflection, no managed-process spawn). Re-use it directly from `RunAsBootstrapper`. NEW class `PreUIPrerequisiteDetector` constructs `WindowsRegistry` + `WindowsFileSystem`, loops `manifest.PreUIPackages`, calls evaluator. Returns list of `MissingPreUIPackage(Id, DisplayName, SourcePath, Arguments)`.

Do NOT reuse `PackageDetector` — it depends on the wider phase-based pipeline.

### 2.5 Elevation — Decision: deferred-elevation, relaunch self when needed

Three candidates:

- A. Always launch Elevation companion before UI. Single UAC at .exe launch, even when no prereq missing. Annoying UX.
- B. Spawn Elevation companion only when prereq missing. Two prompts (engine elevate + maybe UI installer's own).
- C. **Self-relaunch elevated**: if any missing pre-UI prereq detected AND current process not elevated, show TaskDialog "Administrator rights required to install <X>. Continue?" → relaunch own `.exe` with `runas` verb via `ShellExecuteEx`. Elevated instance does the install, then continues to UI launch.

Pick **C**. Matches VS bootstrapper behaviour. Zero prompts when nothing missing. One UAC prompt when something missing. No need to wire pre-UI through the existing `Engine.Elevation` companion (that one is per-package, scoped to MSI ops mid-pipeline — too heavy for this pre-stage).

Add helper `ElevationProbe.IsElevated()` (P/Invoke `OpenProcessToken` + `GetTokenInformation(TokenElevation)`). AOT-safe.

Relaunch flag: `--bootstrap-elevated` so the elevated child knows it must skip the elevation check and proceed straight to install. Pass extraction cache dir so child doesn't re-extract.

### 2.6 Cancel — Decision: TaskDialog cancel → cleanup + exit nonzero

User clicks Cancel on TaskDialog mid-prereq-install:
1. Set `_cancelRequested = true` on the install driver.
2. Kill the prereq installer child process (`Process.Kill(entireProcessTree: true)`).
3. Optionally invoke prereq's own `/uninstall` path if known (best-effort).
4. Delete extraction cache dir.
5. Exit code 2 (Cancelled), matching pipeline cancel semantics.
6. Do NOT launch UI.

### 2.7 Reboot — Decision (LOCKED 2026-05-11): default `IgnoreAndContinue` for 3010

Prereq install returns exit code 3010 (reboot required) or 1641 (immediate reboot initiated):
- 1641: child reboots. Engine exits. No UI.
- 3010: **default behaviour — log a warning, continue to UI launch**. Trust that the runtime is functional enough to host the WPF UI; reboot is typically required for shell/extension registration, not for runtime use.
- `PreUIRebootBehavior.Prompt` and `PreUIRebootBehavior.Block` remain available per-prereq for cases where 3010 truly means "do not continue" (e.g., VC++ Redist x64 + x86 dual install).

Per-prereq override via builder:
```csharp
.PreUIPrerequisite("vcredist_x64.exe", p => p.RebootBehavior(PreUIRebootBehavior.Block))
```

Edge: multiple pre-UI prereqs, mid-list reboot-required. With default `IgnoreAndContinue`, continue down the list. With `Block`, stop and exit 3.

### 2.8 Failure — Decision: TaskDialog modal error, no UI launch, exit 1

Prereq install fails (vital, non-zero non-reboot exit):
- TaskDialog (error icon) showing prereq id + exit code + log path.
- Exit 1 (Failed). UI never launches.
- Cache dir kept on disk for diagnostics; path printed.

## 3. Builder API

### 3.1 Surface — Decision: Option A (dedicated method)

```csharp
.PreUIPrerequisite(@"dotnet-runtime-10.0-win-x64.exe", p => p
    .Id("DotNet10Desktop")
    .DisplayName(".NET 10 Desktop Runtime (x64)")
    .Arguments("/quiet /norestart")
    .SearchCondition(sc => sc.RegistryValue(
        RegistryRoot.LocalMachine,
        @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App",
        "10.0.0", "=", "10.0.0"))
    .RemotePayload(
        "https://download.microsoft.com/.../windowsdesktop-runtime-10.0.0-win-x64.exe",
        sha256: "<hash>", size: 56_700_000))
```

Why A over Option B (flag on chain):
- Pre-UI prereqs have a strictly smaller surface: no `InstallCondition` (it IS the condition), no `EnableFeatureSelection`, no `Container`, no `SlipstreamTarget`. Reusing `BundlePackageBuilder` would expose ~12 methods that silently no-op.
- Separation matches `BundleModel.PreUIPackages` decision (2.2).
- Easier to add new pre-UI-only knobs later (RebootBehavior, ProgressMode marquee vs percent) without polluting the chain builder.

New types:
- `PreUIPackageModel` (in `FalkForge.Compiler.Bundle`)
- `PreUIPackageBuilder` (in `FalkForge.Compiler.Bundle.Builders`)
- `BundleBuilder.PreUIPrerequisite(string sourcePath, Action<PreUIPackageBuilder> configure)` method
- `BundleBuilder._preUIPackages` field, propagated into `BundleModel.PreUIPackages`

Note: `BuiltInPrerequisites` static class gains `DotNet10Desktop()`, `DotNet10DesktopAsPreUI()` helpers returning a pre-configured `PreUIPackageBuilder`-shaped model. .NET Framework 4.x stays in regular chain (UI doesn't need it).

## 4. Manifest Changes

`InstallerManifest.cs` (additive):

```csharp
public PreUIPackageInfo[] PreUIPackages { get; init; } = [];
```

New `PreUIPackageInfo` (`src/FalkForge.Engine.Protocol/Manifest/PreUIPackageInfo.cs`):

```csharp
public sealed class PreUIPackageInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string SourcePath { get; init; }     // relative to extraction dir
    public required string Sha256Hash { get; init; }
    public required string Arguments { get; init; }
    public IReadOnlyList<SearchCondition> SearchConditions { get; init; } = [];
    public string? DownloadUrl { get; init; }            // null = embedded payload
    public long Size { get; init; }
    public PreUIRebootBehavior RebootBehavior { get; init; } = PreUIRebootBehavior.IgnoreAndContinue;
    public PreUIPayloadMode PayloadMode { get; init; } = PreUIPayloadMode.Embedded;
    public IReadOnlyDictionary<int, ExitCodeBehavior>? ExitCodes { get; init; }
}

public enum PreUIRebootBehavior { IgnoreAndContinue, Prompt, Block }
public enum PreUIPayloadMode { Embedded, Remote }
```

Register in `LayoutJsonContext`:

```csharp
[JsonSerializable(typeof(PreUIPackageInfo))]
[JsonSerializable(typeof(PreUIPackageInfo[]))]
```

Back-compat: default `[]` means old bundles or bundles with no pre-UI prereqs run unchanged.

## 5. Engine Bootstrap Flow Rewrite

`Program.RunAsBootstrapper` revised sequence:

```
1. Extract bundle to cacheDir, deserialize manifest.       (existing)
2. If manifest.PreUIPackages.Length > 0:
   2a. PreUIPrerequisiteDetector.FindMissing(manifest.PreUIPackages) -> List<PreUIPackageInfo>
   2b. If list empty -> goto step 4.
   2c. If !ElevationProbe.IsElevated():
        - TaskDialog "Setup needs to install N components requiring administrator rights." Buttons: Continue / Cancel.
        - If Cancel -> exit 2.
        - ShellExecuteEx(verb="runas", file=Environment.ProcessPath, args="--bootstrap-elevated --cache-dir <cacheDir>")
        - WaitForExit. Forward child exit code.
        - If child exit == 0 -> goto step 4 (still launch UI from the unelevated parent — UI runs unelevated, Elevation companion handles per-package privilege escalation).
   2d. Else (we ARE elevated, either originally or via relaunch):
        - PreUIInstaller.RunAll(missing, taskDialogProgress):
            For each missing pkg:
              - Resolve payload: cache file or download (RemotePayload) with HTTP+SHA256 (reuse PayloadDownloader pattern).
              - Spawn process with Arguments. Wait. Handle exit codes (Success / Reboot3010 / Reboot1641 / Cancel / Failure).
              - On failure: show error TaskDialog, exit 1.
              - On 3010/1641: handle per RebootBehavior.
              - On cancel: kill, exit 2.
3. (Implicit: if elevated relaunch path, child exits here; parent unelevated continues at step 4.)
4. Launch UI process (existing Process.Start at line 413).
5. RunUntilShutdown (existing).
```

Cache-dir handoff: when elevated child gets `--cache-dir`, it skips extraction (already done by parent). Verify extraction integrity by re-hashing manifest.json against in-bundle copy — defence against tampering between extraction and elevation prompt.

### 5.1 New helper classes (under `FalkForge.Engine.Bootstrap` namespace)

- `PreUIPrerequisiteDetector` — `FindMissing(PreUIPackageInfo[]) -> List<PreUIPackageInfo>`. Uses `SearchConditionEvaluator`.
- `PreUIPrerequisiteInstaller` — `RunAll(List<PreUIPackageInfo>, IProgressSink, CancellationToken) -> PreUIResult`.
- `TaskDialogProgress` — wraps comctl32 TaskDialog, owns the dialog thread, exposes `SetTitle(string)`, `SetMessage(string)`, `SetPercent(int)`, `OnCancel` event.
- `NativeTaskDialogMethods` — P/Invoke TaskDialogIndirect + structs + callback typedef.
- `ElevationProbe` — `IsElevated()` via OpenProcessToken/TokenElevation.
- `ElevatedSelfRelauncher` — ShellExecuteEx with runas verb, WaitForExit, exit-code forward.
- `PreUIPayloadResolver` — local file vs RemotePayload download. Reuse `PayloadDownloader` if it works in pre-UI context (verify no UI dependency).

All AOT-safe. No reflection. Manual DI (no `IServiceProvider`). All P/Invokes via `LibraryImport` (source-generated marshalling).

## 6. Compiler Changes

`BundleCompiler.Compile` (and indirectly `ManifestGenerator`):
- Hash pre-UI source files; populate `PreUIPackageInfo.Sha256Hash`.
- Embed pre-UI payloads in the bundle TOC as a distinct payload-kind (e.g. `TocEntryKind.PreUIPayload`) so `BundleReader.Extract` can identify and lay them out into a dedicated subdirectory (`<cacheDir>/preui/<id>.exe`) before manifest emission.
- Alternative for `RemotePayload` pre-UI: do NOT embed; `PreUIPayloadResolver` downloads at install time. Big bundle size saving.
- `BundleValidator` new rules:
  - `BDL026` Pre-UI prereq must have at least one SearchCondition.
  - `BDL027` Pre-UI prereq must have non-empty Arguments OR be MSI (in which case we'd inject `/qn /norestart`).
  - `BDL028` Pre-UI prereq must be embedded payload OR RemotePayload (not both null).

## 7. Files Affected

### Protocol / manifest (additive)
- `src/FalkForge.Engine.Protocol/Manifest/PreUIPackageInfo.cs` — NEW.
- `src/FalkForge.Engine.Protocol/Manifest/PreUIRebootBehavior.cs` — NEW enum.
- `src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs` — add `PreUIPackages` property.
- `src/FalkForge.Engine/Layout/LayoutJsonContext.cs` — register new types.

### Engine bootstrap flow (rewrite)
- `src/FalkForge.Engine/Program.cs` — refactor `RunAsBootstrapper` (line 307). Add `--bootstrap-elevated`, `--cache-dir` args. Insert pre-UI step.
- `src/FalkForge.Engine/Bootstrapper.cs` — extend `BuildUiArgs` if any new flags must propagate.
- `src/FalkForge.Engine/Bootstrap/PreUIPrerequisiteDetector.cs` — NEW.
- `src/FalkForge.Engine/Bootstrap/PreUIPrerequisiteInstaller.cs` — NEW.
- `src/FalkForge.Engine/Bootstrap/PreUIPayloadResolver.cs` — NEW.
- `src/FalkForge.Engine/Bootstrap/ElevationProbe.cs` — NEW.
- `src/FalkForge.Engine/Bootstrap/ElevatedSelfRelauncher.cs` — NEW.
- `src/FalkForge.Engine/Bootstrap/PreUIResult.cs` — NEW (Success / Cancelled / Failed / RebootRequired discriminated union).

### Native TaskDialog module (new)
- `src/FalkForge.Engine/Bootstrap/Native/NativeTaskDialogMethods.cs` — NEW: P/Invoke TASKDIALOGCONFIG, TaskDialogIndirect, TASKDIALOG_NOTIFICATIONS, callback delegate, TDM_* messages.
- `src/FalkForge.Engine/Bootstrap/Native/TaskDialogProgress.cs` — NEW: managed wrapper, owns dialog thread + cancel event.
- `src/FalkForge.Engine/app.manifest` — NEW or extend existing: comctl32 v6 + `requestedExecutionLevel level="asInvoker"`.
- `src/FalkForge.Engine/FalkForge.Engine.csproj` — wire `<ApplicationManifest>app.manifest</ApplicationManifest>`. Verify AOT publish embeds manifest.

### Builder API
- `src/FalkForge.Compiler.Bundle/Models/PreUIPackageModel.cs` — NEW.
- `src/FalkForge.Compiler.Bundle/Builders/PreUIPackageBuilder.cs` — NEW.
- `src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs` — add `_preUIPackages` field, `PreUIPrerequisite(...)` method, `Build()` propagation.
- `src/FalkForge.Compiler.Bundle/BundleModel.cs` — add `IReadOnlyList<PreUIPackageModel> PreUIPackages` property.
- `src/FalkForge.Compiler.Bundle/Prerequisites/BuiltInPrerequisites.cs` — add `DotNet10Desktop()` returning configured `PreUIPackageBuilder`-shaped model (consider separate type `PreUIPackageGroupModel` if needed).

### Compiler
- `src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs` — emit `manifest.PreUIPackages` from `model.PreUIPackages`.
- `src/FalkForge.Compiler.Bundle/Compilation/PayloadEmbedder.cs` — embed pre-UI payloads (or skip if RemotePayload-only).
- `src/FalkForge.Compiler.Bundle/Compilation/BundleCompiler.cs` — wire pre-UI through.
- `src/FalkForge.Compiler.Bundle/Compilation/TocEntry.cs` — optional `IsPreUI : bool` flag OR new kind.
- `src/FalkForge.Engine/Layout/BundleReader.cs` — extract pre-UI payloads into `<cacheDir>/preui/` subdir; populate the resolved local paths the detector/installer will use.
- `src/FalkForge.Compiler.Bundle/Validation/BundleValidator.cs` — new rules BDL026-028.

### Demo update
- `demo/MAS/bundle/Program.cs` — promote `.NET 10 Desktop Runtime` to `.PreUIPrerequisite(...)` (UI needs it). Keep `BuiltInPrerequisites.OdbcDriver17()` + `SqlExpress2017()` in `.Chain(...)` as regular prereqs (UI doesn't need ODBC/SQL to render). NetFx472 + VCRedist14x64 stay in chain unless WPF UI actually needs VCRedist — verify; if WPF doesn't depend on VCRedist for managed code, keep it as regular prereq.

### Tests (per-layer xUnit)
- `tests/FalkForge.Engine.Tests/Bootstrap/PreUIPrerequisiteDetectorTests.cs` — NEW.
- `tests/FalkForge.Engine.Tests/Bootstrap/PreUIPrerequisiteInstallerTests.cs` — NEW (mock `IProcessRunner`).
- `tests/FalkForge.Engine.Tests/Bootstrap/ElevationProbeTests.cs` — NEW (Windows-only).
- `tests/FalkForge.Engine.Tests/BootstrapperArgsTests.cs` — extend for `--bootstrap-elevated`, `--cache-dir`.
- `tests/FalkForge.Engine.Protocol.Tests/Manifest/InstallerManifestRoundTripTests.cs` — add PreUIPackages JSON round-trip.
- `tests/FalkForge.Compiler.Bundle.Tests/Builders/PreUIPackageBuilderTests.cs` — NEW.
- `tests/FalkForge.Compiler.Bundle.Tests/Compilation/ManifestGeneratorPreUITests.cs` — NEW.
- `tests/FalkForge.Compiler.Bundle.Tests/Validation/BundleValidatorPreUITests.cs` — NEW (BDL026/027/028).
- `tests/FalkForge.Integration.Tests/PreUIBootstrapEndToEndTests.cs` — NEW (full pipeline: build bundle with pre-UI .NET 10 stub, extract, verify detect path).
- `tests/FalkForge.Engine.Tests/Bootstrap/Native/TaskDialogProgressTests.cs` — NEW (smoke; may need [Trait("Category","Manual")] for actual dialog).

## 8. Effort Estimate

| Step | Size | Notes |
|------|------|-------|
| Manifest types + JSON context | S | Trivial additive |
| Builder API (`PreUIPackageBuilder`, BundleBuilder method, BundleModel field) | S | |
| BundleValidator rules BDL026-028 | S | |
| ManifestGenerator + PayloadEmbedder wiring | S | |
| BundleReader pre-UI extraction | S | |
| `PreUIPrerequisiteDetector` + tests | S | Reuses evaluator |
| `ElevationProbe` + `ElevatedSelfRelauncher` + tests | M | P/Invoke + ShellExecuteEx + UAC integration tests |
| `NativeTaskDialogMethods` + `TaskDialogProgress` + tests | M | TaskDialog quirks (apartment threading, callback marshalling, manifest dependency) |
| `PreUIPrerequisiteInstaller` + payload resolver + tests | M | Process exit codes, cancel, reboot path |
| `Program.RunAsBootstrapper` rewrite + integration | M | Sequencing, --bootstrap-elevated relaunch protocol |
| comctl32 v6 manifest in Engine + AOT publish verification | S | One-time wiring + smoke test |
| MAS demo migration | S | |
| BuiltInPrerequisites .NET 10 helper | S | |
| Integration test (full E2E build + extract + simulated missing .NET) | M | Mock filesystem/registry |
| Documentation (CLAUDE.md, documentation.html section) | S | Defer; tracked in regen plan |

**Total: ~7-10 engineering days** (rolled up: 9 S × 0.5d + 5 M × 2d = 4.5 + 10 = ~14.5 person-days raw, realistic 7-10 calendar days with TDD overhead).

## 9. Open Questions — RESOLVED 2026-05-11

| # | Question | Decision |
|---|----------|----------|
| 1 | Elevation policy | **Self-relaunch on demand** (decision 2.5) — no UAC when nothing missing, one UAC prompt when prereq detected, ShellExecuteEx 'runas' relaunch. |
| 2 | Reboot policy | **Default `IgnoreAndContinue` for 3010** (decision 2.7 updated). Per-prereq override via `.RebootBehavior(...)` builder method allows `Prompt` / `Block` for stricter prereqs. |
| 3 | RemotePayload | **Configurable per-prereq, default `Embedded`** (new `PreUIPayloadMode` enum). Builder method `.RemotePayload(url, sha256, size)` switches mode to `Remote` for that one prereq. |
| 4 | Engine.Elevation reuse | **Not reused** — pre-UI keeps separate self-relaunch path. Plan unchanged. |
| 5 | TaskDialog localization | **Deferred** — tracked as Phase 3+ follow-up. Hardcoded en-US strings in v0.2. |
| 6 | comctl32 v6 manifest collision | **Verify during implementation** — `FalkForge.Engine.csproj` currently has no `<ApplicationManifest>`; new `app.manifest` ships clean (no collision). |

| # | Question | Decision |
|---|----------|----------|
| 4 | Ship phasing | **Foundation+Detect first (v0.1), then UI+Install (v0.2)** (decision §11). |

## 10. TDD Spec — Failing-Test Order

This is the RED -> GREEN sequence the implementer follows. Each row = one failing test commit, then one minimal-impl commit.

| # | Test | Purpose / Asserts |
|---|------|-------------------|
| 1 | `PreUIPackageInfo_Serializes_Roundtrip` | new manifest type + LayoutJsonContext registration |
| 2 | `InstallerManifest_PreUIPackages_DefaultsToEmpty` | back-compat; missing field deserializes to `[]` |
| 3 | `PreUIPackageBuilder_Build_PopulatesAllFields` | builder API exists |
| 4 | `BundleBuilder_PreUIPrerequisite_AddsToModel` | wiring into `BundleModel.PreUIPackages` |
| 5 | `BundleValidator_BDL026_RequiresSearchCondition` | validation rule |
| 6 | `BundleValidator_BDL027_RequiresArgumentsOrMsi` | validation rule |
| 7 | `BundleValidator_BDL028_RequiresEmbeddedOrRemotePayload` | validation rule |
| 8 | `ManifestGenerator_EmitsPreUIPackages` | compiler emits manifest field |
| 9 | `PayloadEmbedder_EmbedsPreUIPayloads` | bundle TOC contains pre-UI bytes |
| 10 | `BundleReader_ExtractsPreUIPayloadsIntoSubdir` | layout on disk after extract |
| 11 | `PreUIPrerequisiteDetector_DetectsMissing_WhenRegistryAbsent` | uses SearchConditionEvaluator |
| 12 | `PreUIPrerequisiteDetector_DetectsInstalled_WhenRegistryPresent` | inverse |
| 13 | `ElevationProbe_ReportsCurrentElevationState` | smoke (Windows-only) |
| 14 | `ElevatedSelfRelauncher_BuildsRelaunchArgs` | args composition, no actual spawn |
| 15 | `TaskDialogProgress_ReportsCancelOnButtonClick` | callback wiring (may need mock/manual) |
| 16 | `PreUIPrerequisiteInstaller_RunsAllSuccessfully` | mock IProcessRunner returning 0 |
| 17 | `PreUIPrerequisiteInstaller_HandlesReboot3010` | exit code 3010 path |
| 18 | `PreUIPrerequisiteInstaller_HandlesCancelMidInstall` | CancellationToken + Process.Kill |
| 19 | `PreUIPrerequisiteInstaller_FailsLoudOnNonZeroExit` | error TaskDialog, exit 1 |
| 20 | `RunAsBootstrapper_SkipsPreUI_WhenNoneMissing` | happy path: UI launches direct |
| 21 | `RunAsBootstrapper_RelaunchesElevated_WhenMissingAndNotElevated` | relaunch arg path |
| 22 | `RunAsBootstrapper_SkipsRelaunch_WhenAlreadyElevatedFlag` | --bootstrap-elevated child path |
| 23 | `RunAsBootstrapper_LaunchesUI_AfterPreUISuccess` | end-to-end ordering |
| 24 | `RunAsBootstrapper_DoesNotLaunchUI_AfterPreUIFailure` | failure path |
| 25 | E2E: `Mas_DemoBundle_PrereqMissing_TriggersPreUIInstall` | integration in `FalkForge.Integration.Tests` |

## 11. Phasing (Recommended Ship Order)

Phase 1 — Foundation (ships as usable subset): rows 1–10 of TDD table.
- Manifest types, builder API, validator, compiler emission, bundle reader extraction.
- No native UI yet. Engine still launches WPF UI without prereq check.
- Value: pre-UI prereqs declarable + serializable + buildable; field rollout possible behind a feature flag.

Phase 2 — Detection: rows 11–12.
- Engine can probe at startup, log missing list, but still launches UI (just adds a warning log line).
- Useful diagnostic alone; lets us collect telemetry on real-world miss rates.

Phase 3 — Native UI + install: rows 13–19.
- TaskDialog + installer + elevation. End-to-end pre-UI install works.
- This is the headline shipping unit.

Phase 4 — Wire into `RunAsBootstrapper` + demo migration: rows 20–25.
- Flip the switch. MAS bundle adopts. Documentation regen entry queued.

Ship Phase 1+2 together as v0.1 (low risk, no behavioural change). Phase 3+4 as v0.2 (behavioural switch — needs full regression on existing demos).

## 12. Out of Scope (Per Prompt)

- Implementation code.
- Self-contained UI publish as alternative (user ruled out).
- Migrating non-MAS demos.
- TaskDialog localization (follow-up).

---

### Critical Files for Implementation

- `D:/Git/FalkInstaller/src/FalkForge.Engine/Program.cs`
- `D:/Git/FalkInstaller/src/FalkForge.Engine.Protocol/Manifest/InstallerManifest.cs`
- `D:/Git/FalkInstaller/src/FalkForge.Compiler.Bundle/Builders/BundleBuilder.cs`
- `D:/Git/FalkInstaller/src/FalkForge.Compiler.Bundle/Compilation/ManifestGenerator.cs`
- `D:/Git/FalkInstaller/src/FalkForge.Engine/Detection/SearchConditionEvaluator.cs`
