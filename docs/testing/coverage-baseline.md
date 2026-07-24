# Coverage Baseline

Source-only (production assemblies, no test/demo assemblies) code coverage baseline for
FalkForge, refreshed as part of assessment priority P3 ("refresh the stale coverage
baseline + wire mutation testing").

There are **no enforced coverage thresholds** in this repo (no CI gate fails on a
coverage number). This document is a point-in-time record for tracking trend, not a
quality gate.

## How to reproduce

```powershell
pwsh scripts/coverage.ps1                 # -Configuration defaults to Release
```

The script:

1. `dotnet build FalkForge.slnx -c Release`
2. Runs the full test suite wrapped in `dotnet-coverage collect`, writing
   `TestResults/coverage.cobertura.xml`
3. Runs `reportgenerator` against that cobertura file with
   `-assemblyfilters:+FalkForge.*;-FalkForge.*.Tests;-FalkForge.*.Tests.*;-*Demo*`
   (src-only), emitting `CoverageReport/Summary.json`, `CoverageReport/Summary.txt`,
   and `CoverageReport/index.html`

**Why `dotnet-coverage` instead of `dotnet test --collect:"XPlat Code Coverage"`:**
this repo's test projects run on xunit.v3 / Microsoft.Testing.Platform (MTP), not
classic VSTest. Under MTP the coverlet.collector data collector never attaches, so
`--collect` is a silent no-op — it exits successfully and produces no coverage data at
all. `dotnet-coverage collect` wraps the whole `dotnet test` invocation externally and
attaches via the CLR profiler instead, which works regardless of which test host runs
underneath.

## Gating caveat

This baseline is a **default local run**: `FALKFORGE_E2E` and
`FALKFORGE_REAL_SYSTEM_E2E` were both unset, so the opt-in heavyweight E2E suites
(whole demo catalog, `forge verify --rebuild`, SignServer container, real-machine
mutation tests) did **not** run and their code paths are undercounted here.

CI's coverage job sets `FALKFORGE_E2E=1`, so **CI's coverage numbers are not directly
comparable to this baseline** — CI exercises more code paths and will read higher in
places this document shows as gaps. This document does not restate CI's numbers; only
this local default-gated run is recorded below.

## Current baseline

| Date | Line | Branch | Method | Full-method | Assemblies | Classes |
|---|---|---|---|---|---|---|
| 2026-07-24 | 77.3% (58,864 / 76,114) | 70.9% (19,625 / 27,656) | 78.3% (5,413 / 6,909) | 60.1% (4,156 / 6,909) | 28 | 1,168 |

Delta vs the **2026-07-20 refresh** (same script, same default gating, same
`-assemblyfilters`, 28 src assemblies both times — the true like-for-like baseline):
**line 78.3% → 77.3% (−1.0pp)**, **branch 71.9% → 70.9% (−1.0pp)**. This is a decline,
not an improvement — say so plainly rather than calling it "essentially flat."

Context that makes the decline interpretable: coverable lines grew 69,893 → 76,114
(+6,221) between the two runs, while covered lines grew only 54,727 → 58,864 (+4,137).
That means the ~6,221 lines added since 2026-07-20 landed at roughly **66.5%** line
coverage (4,137 / 6,221) — below the 78.3% average of the code that existed before, so
the aggregate percentage drops even though the absolute number of covered lines went
up. The same pattern holds for branches: coverable branches grew 25,102 → 27,656
(+2,554), covered branches grew only 18,073 → 19,625 (+1,552), i.e. new branches landed
at roughly **60.8%** (1,552 / 2,554) versus the prior 71.9% average. Some of that new,
under-tested code is visible directly in the hot-spot list below — e.g.
`BootstrapperRunner` at 0%, extracted during the Program.cs decomposition and exercised
only by the opt-in E2E suites, not unit tests.

The 2026-07-09 baseline (line 76.07% / branch 68.79%, 27 assemblies) is kept below only
as a historical data point — it was a **full-suite run with `FALKFORGE_E2E=1`**, so it
is not gating-comparable to either the 2026-07-20 or 2026-07-24 runs above and must not
be used for delta math.

## Per-assembly line/branch coverage

Sorted ascending by line coverage. Source: `CoverageReport/Summary.json`.

| Assembly | Line % | Branch % | Covered / Coverable lines | Classes |
|---|---|---|---|---|
| FalkForge.Cli | 48.7 | 53.8 | 6,128 / 12,581 | 83 |
| FalkForge.Compiler.Msix | 59.7 | 50.9 | 495 / 828 | 20 |
| FalkForge.Studio | 65.4 | 53.0 | 2,107 / 3,219 | 114 |
| FalkForge.Engine.Elevation | 68.7 | 56.6 | 641 / 933 | 17 |
| FalkForge.Plugins.Sql | 71.4 | 64.8 | 115 / 161 | 5 |
| FalkForge.Plugins.FileSystem | 71.4 | 50.0 | 20 / 28 | 2 |
| FalkForge.Signing.SignServer | 74.5 | 72.5 | 266 / 357 | 5 |
| FalkForge.Ui | 77.3 | 72.5 | 4,681 / 6,050 | 55 |
| FalkForge.Extensibility | 77.5 | 79.2 | 76 / 98 | 11 |
| FalkForge.Extensions.Firewall | 78.5 | 56.0 | 348 / 443 | 12 |
| FalkForge.Platform.Windows | 78.9 | 91.1 | 120 / 152 | 7 |
| FalkForge.Engine | 82.3 | 77.2 | 9,550 / 11,593 | 156 |
| FalkForge.Compiler.Msi | 82.6 | 70.4 | 9,192 / 11,125 | 164 |
| FalkForge.Engine.Protocol | 82.6 | 77.3 | 7,171 / 8,681 | 124 |
| FalkForge.Plugins.Odbc | 83.0 | 66.6 | 59 / 71 | 5 |
| FalkForge.Decompiler | 85.0 | 73.2 | 2,190 / 2,574 | 51 |
| FalkForge.Extensions.Sql | 87.1 | 79.9 | 871 / 1,000 | 22 |
| FalkForge.Extensions.Driver | 87.7 | 89.4 | 100 / 114 | 6 |
| FalkForge.Testing | 88.6 | 70.6 | 235 / 265 | 15 |
| FalkForge.Compiler.Bundle | 89.1 | 86.7 | 6,055 / 6,792 | 47 |
| FalkForge.Localization | 89.9 | 84.6 | 224 / 249 | 9 |
| FalkForge.Extensions.Dependency | 90.4 | 80.7 | 455 / 503 | 16 |
| FalkForge.Extensions.Util | 90.6 | 92.3 | 1,076 / 1,187 | 32 |
| FalkForge.Ui.Abstractions | 90.9 | 83.8 | 190 / 209 | 6 |
| FalkForge.Extensions.Iis | 93.6 | 83.1 | 912 / 974 | 20 |
| FalkForge.Extensions.DotNet | 93.9 | 84.2 | 295 / 314 | 14 |
| FalkForge.Core | 94.1 | 86.6 | 5,126 / 5,447 | 143 |
| FalkForge.Extensions.Http | 100.0 | 96.2 | 166 / 166 | 7 |

## Lowest-covered hot spots (classes under 50% line coverage)

Classes with fewer than 5 coverable lines are dropped as noise (near-empty
records/markers whose percentage swings on a single line). The one source-generated
class in range, `FalkForge.Cli.Diff.PlanDiffManifestJsonContext` (System.Text.Json
source generator output, 0%, 4,465 coverable lines), is excluded as generated code —
its numbers are an artifact of the generated partial method bodies, not hand-written
logic.

Classification key: **(a)** genuinely untested logic worth a test, **(b)** exercised
only by the opt-in `FALKFORGE_E2E` / `FALKFORGE_REAL_SYSTEM_E2E` suites (so this
default run undercounts it), **(c)** thin P/Invoke, COM, process-launch, or WPF
view/XAML shim that is hard or low-value to unit test, **unclassified** where neither
could be determined without deeper investigation than this pass allowed.

### FalkForge.Cli

| Class | Line % | Note |
|---|---|---|
| Verification.DefaultRebuildRunner | 2.8 | (b) drives `forge verify --rebuild`, the heavyweight opt-in rebuild-and-diff path |
| Settings.MigrateSettings | 7.6 | (c) thin Spectre CLI settings record, declarative properties |
| MsiExtractor | 9.4 | (a) genuinely untested — historically the site of a real zip-slip fix; only the happy path is exercised |
| Settings.RulesListSettings | 11.1 | (c) thin Spectre CLI settings record |
| Settings.BundleReattachSettings | 18.7 | (c) thin Spectre CLI settings record |
| Commands.ValidateCommand | 20.0 | (a) genuinely untested — real branching (19.3% branch cov) in the command body |
| Commands.InspectCommand | 30.0 | (a) genuinely untested command logic |
| Commands.PlanDiffCommand | 34.4 | (a) genuinely untested command logic |
| ScriptLoader | 40.4 | (a) genuinely untested SQL-script loading logic |

### FalkForge.Compiler.Msi

| Class | Line % | Note |
|---|---|---|
| DryRunSidecarWriter | 0.0 | (a) no dedicated tests found; the `--dry-run` sidecar-writing path looks genuinely untested |
| Interop.FciHandle | 0.0 | (c) thin native-handle wrapper around the Cabinet FCI/FDI interop, no branches |
| PatchCompiler | 0.0 | (a) genuinely untested — no unit test file exists; the MSP output path is only referenced as a "stub" comment in an integration payload provisioner |
| TransformCompiler | 0.0 | (a) genuinely untested — same situation as PatchCompiler; the MST output path is unverified |
| MsmCompiler | 8.6 | (a) has a dedicated test file (`MsmCompilerTests.cs`) but the msi.dll P/Invoke build path is barely exercised |
| UI.MsiControlEvent | 22.2 | unclassified — small UI dialog-event data type, unclear why undertested |
| Validation.IceValidator | 34.4 | (a) has dedicated tests (`IceValidatorTests.cs`, `IceValidatorConfigTests.cs`) but real validation branches (28.5% branch cov) remain thin |
| Signing.CodeSigner | 36.6 | (a) has tests plus a mutation-test file, yet branch coverage (32.1%) is low — undertested signing paths |
| Cabinets.FileSystemCabinetChainResolver | 40.0 | unclassified — small resolver, no obvious reason for the gap |

### FalkForge.Compiler.Msix

| Class | Line % | Note |
|---|---|---|
| Packaging.AppxPackageWriter | 0.0 | (c) COM/AppxPackaging interop shim (`IStream` wrapper), hard to unit test without a real AppxPackaging host |
| Packaging.AppxPackageWriter.ComStreamWrapper | 0.0 | (c) nested COM interop shim |
| MsixBundleCompiler | 10.7 | (a) has direct unit tests (`MsixBundleBuilderTests.cs`), yet coverage is still low — undertested |
| MsixCompiler | 22.0 | (a) has a dedicated test file (`MsixCompilerTests.cs`), still undertested |

### FalkForge.Core

| Class | Line % | Note |
|---|---|---|
| Installer | 36.1 | (a) genuinely undertested — this is the primary fluent-API entry type, worth prioritizing |

### FalkForge.Decompiler

| Class | Line % | Note |
|---|---|---|
| WixBurnAccess | 1.8 | (b) low-level Burn-container access, consistent with being exercised mainly by the opt-in decompile/round-trip E2E suites rather than unit tests |

### FalkForge.Engine

| Class | Line % | Note |
|---|---|---|
| RestartManager.RestartManagerSession | 0.0 | (c) thin P/Invoke wrapper over the Windows Restart Manager API |
| Bootstrap.Native.NativeDialogDriver | 0.0 | (c) native dialog P/Invoke shim |
| Bootstrap.TaskDialogProgressSink | 0.0 | (c) native TaskDialog P/Invoke shim |
| Bootstrap.WindowsFileSystemProvider | 0.0 | (c) thin OS filesystem shim |
| BootstrapperRunner | 0.0 | (c) process bootstrap/entry-point shim — launches the real bundle process, not naturally unit-testable |
| Elevation.ProcessLauncher | 0.0 | (c) thin process-launch shim |
| Execution.ProcessRunner | 0.0 | (c) thin process-launch shim |
| Program | 0.0 | (c) `Main` entry point |
| Pipeline.NullUiChannel | 28.5 | unclassified — small null-object UI channel stub |
| Bootstrap.ElevatedSelfRelauncher | 30.4 | (a) genuinely undertested — security-relevant elevation-relaunch logic; branch cov (62.5%) is partial |
| Cache.PackageCache | 43.1 | (a) has a dedicated test file (`PackageCacheSignatureTests.cs`) but line/branch coverage remain low |
| Pipeline.NamedPipeElevationGateway | 47.5 | (a) has a dedicated test file (`NamedPipeElevationGatewayTests.cs`), still undertested |

### FalkForge.Engine.Elevation

| Class | Line % | Note |
|---|---|---|
| Commands.MsiProgressState | 0.0 | unclassified — small progress-state record, unclear why untested |
| Program | 0.0 | (c) `Main` entry point |
| ElevatedHost | 46.8 | (a) has a dedicated test file (`ElevatedHostCommandSurfaceTests.cs`) but this security-relevant elevation host is still undertested |
| Commands.ServiceInstallCommand | 48.7 | (a) has a dedicated test file (`ServiceInstallCommandTests.cs`), still undertested |

### FalkForge.Engine.Protocol

| Class | Line % | Note |
|---|---|---|
| Bundle.BundleReader.BoundedReadStream | 25.0 | (a) genuinely undertested — this is the stream-bounds-checking guard tied to the historical oversized-manifest trust-bypass fix; worth closing the gap |
| Transport.PipeDisconnectedException | 33.3 | (c) trivial custom exception type (constructor overloads) |

### FalkForge.Extensibility

| Class | Line % | Note |
|---|---|---|
| FalkForgeVersion | 0.0 | (c) thin static version/constant holder |
| PluginCompatibilityException | 33.3 | (c) trivial custom exception type |

### FalkForge.Extensions.Driver

| Class | Line % | Note |
|---|---|---|
| DriverExtension | 46.1 | (a) genuinely undertested — related builder/validator classes have tests, but this `IMsiTableContributor` entry class itself is only partly covered |

### FalkForge.Platform.Windows

| Class | Line % | Note |
|---|---|---|
| WindowsMsiApi | 0.0 | (c) thin P/Invoke facade |
| NativeMethods | 31.5 | (c) raw P/Invoke declarations; coverage is a byproduct of which native calls happen to be exercised |

### FalkForge.Signing.SignServer

| Class | Line % | Note |
|---|---|---|
| SignServerConfig | 42.1 | (a) has a dedicated test file (`SignServerConfigTests.cs`) but branch coverage is only 7.1% — config-parsing branches are largely untested |

### FalkForge.Studio

| Class | Line % | Note |
|---|---|---|
| Export.CiCdExportDialog | 0.0 | (c) WPF dialog code-behind |
| Editors.ProductEditor.ProductEditorView | 0.0 | (c) WPF view code-behind |
| Editors.OdbcEditor.OdbcEditorView | 0.0 | (c) WPF view code-behind |
| Editors.TableInspector.TableInspectorView | 0.0 | (c) WPF view code-behind |
| Shell.StudioWindow | 0.0 | (c) WPF window code-behind |
| Editors.FilesEditor.FilesEditorView | 0.0 | (c) WPF view code-behind |
| Editors.FeaturesEditor.FeaturesEditorView | 0.0 | (c) WPF view code-behind |
| Editors.DiffViewer.DiffViewerViewModel | 0.0 | unclassified — no view-model logic exercised, unclear if by design |
| Editors.DialogEditor.DialogEditorView | 0.0 | (c) WPF view code-behind |
| Editors.DialogEditor.DialogControlPresenter | 0.0 | (c) WPF presenter tied to designer surface |
| Editors.DependencyGraph.GraphNodeViewModel | 0.0 | unclassified — thin graph-node view-model |
| Editors.DependencyGraph.DependencyGraphViewModel | 0.0 | unclassified — graph view-model, no logic exercised |
| Editors.DependencyGraph.DependencyGraphView | 0.0 | (c) WPF view code-behind |
| Editors.UiEditor.UiEditorView | 0.0 | (c) WPF view code-behind |
| Editors.SqlEditor.SqlEditorView | 0.0 | (c) WPF view code-behind |
| Shell.NewProjectDialog | 0.0 | (c) WPF dialog code-behind |
| Inspect.MsiTableReader | 12.5 | (a) genuinely undertested — real MSI-table-reading logic behind the inspector, not a view |
| Editors.CustomActionsEditor.CustomActionEntryViewModel | 26.6 | (c) thin bindable row wrapper for a list entry |
| Editors.SqlEditor.SqlScriptViewModel | 28.5 | (c) thin bindable row wrapper |
| Editors.OdbcEditor.OdbcDriverEntryViewModel | 33.3 | (c) thin bindable row wrapper |
| RelayCommand | 33.3 | (c) generic `ICommand` helper; most branches only fire through live UI interaction |
| Editors.ShortcutsEditor.ShortcutEntryViewModel | 33.3 | (c) thin bindable row wrapper |
| Editors.OdbcEditor.OdbcDataSourceEntryViewModel | 33.3 | (c) thin bindable row wrapper |
| Editors.ScheduledTasksEditor.ScheduledTaskEntryViewModel | 36.3 | (c) thin bindable row wrapper |
| Editors.XmlConfigEditor.XmlConfigEntryViewModel | 40.0 | (c) thin bindable row wrapper |
| Editors.ServicesEditor.ServiceEntryViewModel | 40.0 | (c) thin bindable row wrapper |
| Editors.FirewallEditor.FirewallRuleViewModel | 40.0 | (c) thin bindable row wrapper |
| Shell.StudioViewModel | 43.7 | (a) genuinely undertested — this is the main shell orchestration view-model, not a thin wrapper (311 coverable lines) |

### FalkForge.Ui

| Class | Line % | Note |
|---|---|---|
| Views.CompletePage | 0.0 | (c) WPF view code-behind |
| PasswordBridge | 0.0 | (c) thin WPF `PasswordBox`-to-binding attached-property shim |
| Views.CustomInstallerWindow | 0.0 | (c) WPF window code-behind |
| Localization.LanguageSelectorControl | 0.0 | (c) WPF control code-behind |
| Converters.BoolToVisibilityConverter | 0.0 | (c) thin XAML value converter |
| App | 0.0 | (c) WPF `Application` entry point (`App.xaml.cs`) |
| Converters.ProgressToWidthConverter | 0.0 | (c) thin XAML value converter |
| Views.InstallDirPage | 0.0 | (c) WPF view code-behind |
| InstallerApp | 3.2 | (a) genuinely undertested — this is the real bootstrapper/orchestration class (156 coverable lines), not a view; worth prioritizing |
| DriveInfoProvider | 3.8 | (c) thin shim over `System.IO.DriveInfo` |
| ViewModels.LogPathActions | 9.5 | (a) genuinely undertested — real log-path resolution logic |
| ViewModels.LicensePageViewModel | 16.6 | (a) genuinely undertested install-wizard page logic |
| ViewModels.FeaturesPageViewModel | 23.0 | (a) genuinely undertested install-wizard page logic |
| BuiltInUiHost | 25.3 | (a) genuinely undertested — hosts the whole built-in UI shell/dialog sequencing |
| ViewModels.ProgressPageViewModel | 26.5 | (a) genuinely undertested install-wizard page logic |
| ViewModels.CompletePageViewModel | 33.3 | (a) genuinely undertested install-wizard page logic |
| Themes.ThemeDetector | 41.6 | (c) thin OS theme-detection shim (registry read) |
| Views.MainWindow | 46.1 | (c) WPF window code-behind |

### FalkForge.Ui.Abstractions

| Class | Line % | Note |
|---|---|---|
| FeatureStateExtensions | 0.0 | (c) thin extension-method helper, likely only exercised through live UI paths |

## How to re-baseline

Re-run `pwsh scripts/coverage.ps1`, then update the date and numbers in the "Current
baseline" table, the per-assembly table, and the hot-spot section above — all in the
same commit as the script run (do not let this file drift from `CoverageReport/`).
