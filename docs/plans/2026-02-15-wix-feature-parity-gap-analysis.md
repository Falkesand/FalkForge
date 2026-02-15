# FalkInstaller vs WiX Toolset 6.0.2 — Feature Gap Analysis

**Date:** 2026-02-15
**Status:** Active
**Purpose:** Track feature parity between FalkInstaller and WiX Toolset 6.0.2
**Last updated:** 2026-02-15 (after Phase 8 merge)

## Current State

FalkInstaller has 26 source projects across 8 completed phases:
- **Phase 1:** Core domain model, platform abstractions, extensibility, MSBuild SDK, MSI compiler, testing utilities
- **Phase 2:** Cabinet P/Invoke, Environment, Fonts, Close Apps, INI files, Permissions, File Associations, Custom Actions, ICE validation, Code signing
- **Phase 3:** Bundle Engine + UI (Engine, Elevation, Protocol, WPF UI, Bundle Compiler)
- **Phase 4:** Core MSI table completeness (RemoveRegistry, ServiceControl/Dependency, MoveFile, DuplicateFile, RemoveFile, CreateFolder, Assembly GAC, CustomTable, FeatureConditions, MediaTemplate, MajorUpgrade)
- **Phase 5:** Bundle engine intelligence (MspPackage, MsuPackage, NestedBundlePackage, ExitCode mapping, Variables/Conditions, RollbackBoundaries, RelatedBundles, Containers, RemotePayload, Layout, PayloadDownloader)
- **Phase 6:** Runtime robustness and MSI UI (rollback execution, Restart Manager, structured logging, sequence scheduling, dialog sets with 5 templates, maintenance mode)
- **Phase 7:** Extensions and output types (Util complete, Firewall, .NET detection, IIS, SQL extensions; MSM/MSP/MST output types)
- **Phase 8:** CLI polish (JSON localization, Spectre.Console CLI tool, MSI decompiler, parallel cabinet creation)

**Total:** 43 projects (26 src + 17 test), ~1,585 tests, zero warnings.

---

## 1. MSI Authoring — Core Tables

| Feature | Status | Notes |
|---------|--------|-------|
| Files & Directories | Implemented | Full support |
| Components & Features | Implemented | Full support |
| Registry (Key, Value) | Implemented | Full support |
| RemoveRegistryKey/Value | Implemented | RemoveRegistryModel, RemoveRegistryBuilder |
| Shortcuts | Implemented | Full support |
| Services (Install, Config) | Implemented | Full support |
| ServiceControl | Implemented | ServiceControlModel, ServiceControlBuilder |
| ServiceDependency | Implemented | ServiceDependencyModel |
| Environment Variables | Implemented | Full support |
| INI Files | Implemented | Full support |
| Fonts | Implemented | Full support |
| Permissions/ACLs | Implemented | Full support |
| File Associations | Implemented | Full support |
| Custom Actions | Implemented | All types: immediate, deferred, rollback, commit |
| Binaries | Implemented | Full support |
| Properties | Implemented | Full support |
| Launch Conditions | Implemented | Full support |
| Upgrades / MajorUpgrade | Implemented | MajorUpgradeModel, MajorUpgradeBuilder |
| Code Signing | Implemented | Full support |
| ICE Validation | Implemented | Full support |
| Cabinet/Compression | Implemented | Single + parallel multi-threaded (ParallelCabinetBuilder) |
| MoveFiles / DuplicateFiles | Implemented | MoveFileModel, DuplicateFileModel + builders |
| RemoveFile / RemoveFolder | Implemented | RemoveFileModel, RemoveFileBuilder |
| CreateFolder | Implemented | CreateFolderModel, CreateFolderBuilder |
| Assembly GAC Registration | Implemented | AssemblyModel, AssemblyType, AssemblyBuilder |
| Custom Tables | Implemented | CustomTableModel, CustomTableBuilder, ColumnOptions, RowBuilder |
| Conditions on Features/Components | Implemented | FeatureConditionModel |
| MediaTemplate | Implemented | MediaTemplateModel, MediaTemplateBuilder |
| COM Registration (Class/ProgId/TypeLib) | Deferred | Explicitly deferred to future phase |
| ODBC (DataSource/Driver) | Missing | Niche use case |
| IsolateComponent | Missing | Niche |
| BindImage | Missing | Niche |
| ReserveCost | Missing | Niche |

## 2. MSI Output Types

| Feature | Status | Notes |
|---------|--------|-------|
| MSI Package (.msi) | Implemented | MsiCompiler |
| EXE Bundle (.exe) | Implemented | BundleCompiler |
| Merge Module (.msm) | Implemented | MsmCompiler, MergeModuleBuilder |
| Patch (.msp) | Implemented | PatchCompiler, PatchBuilder |
| Transform (.mst) | Implemented | TransformCompiler, TransformBuilder |
| Library (.wixlib) | N/A | C# project references replace this |

## 3. MSI UI & Sequences

| Feature | Status | Notes |
|---------|--------|-------|
| MSI-level Dialog Authoring | Implemented | MsiDialogModel, DialogEmitter, IDialogTemplate |
| InstallExecuteSequence | Implemented | SequenceTable, SequenceActionModel, SequenceBuilder |
| InstallUISequence | Implemented | Shared sequence infrastructure |
| AdminExecute/UISequence | Partial | Sequence infrastructure supports it; not explicitly targeted |
| EmbeddedUI | Missing | FalkInstaller uses bundle-level WPF UI |
| Pre-built dialog sets (WixUI_*) | Implemented | 5 templates: Minimal, InstallDir, FeatureTree, Mondo, Advanced |
| Maintenance mode UI | Implemented | MaintenancePageViewModel |

## 4. Bundle/Burn Engine

| Feature | Status | Notes |
|---------|--------|-------|
| MsiPackage | Implemented | MsiExecutor |
| ExePackage | Implemented | Full support |
| .NET Runtime package | Implemented | DotNet extension |
| MspPackage (patches) | Implemented | MspPackageBuilder, MspExecutor |
| MsuPackage (Windows Update) | Implemented | MsuPackageBuilder, MsuExecutor |
| BundlePackage (nested bundles) | Implemented | NestedBundlePackageBuilder, BundleExecutor |
| Chain ordering | Implemented | ChainBuilder |
| Package conditions (InstallCondition) | Implemented | Via ConditionEvaluator + VariableStore |
| ExitCode mapping | Implemented | ExitCodeMapping, ExitCodeBehavior |
| Variables & condition system | Implemented | VariableStore (30+ built-in), ConditionEvaluator (recursive-descent) |
| Rollback Boundaries | Implemented | RollbackBoundaryBuilder, RollbackBoundaryModel |
| Related Bundles (upgrade/addon/patch) | Implemented | RelatedBundleBuilder, RelatedBundleRelation |
| Containers (payload grouping) | Implemented | ContainerBuilder, ContainerModel |
| RemotePayload (download URLs) | Implemented | RemotePayloadModel |
| Layout/offline install | Implemented | LayoutManager, LayoutJsonContext |
| Package cache from URLs | Implemented | PayloadDownloader with retry + SHA256 verification |
| Update feeds | Missing | |
| Embedded bundle mode | Missing | |
| Custom BA SDK (.NET 6+) | Partial | WPF UI exists but no BA SDK for extensibility |
| Themeable standard BA | N/A | FalkInstaller uses WPF + ReactiveUI instead of WiX standard BA |
| Engine-UI bidirectional control | Implemented | Named pipe IPC protocol with 12 message types |

## 5. Build System & Developer Experience

| Feature | Status | Notes |
|---------|--------|-------|
| MSBuild SDK integration | Implemented | FalkInstaller.Sdk |
| C# fluent API | Implemented | FalkInstaller advantage over WiX |
| Preprocessor | N/A | C# replaces WiX preprocessor |
| Localization | Implemented | JSON-based, culture fallback, !(loc.X) resolution |
| Harvesting (glob patterns) | Partial | Wildcard expansion but not full Heat equivalent |
| Fragments / Libraries (.wixlib) | N/A | C# project references replace this |
| CLI tooling | Implemented | `falk` CLI: build, validate, inspect, decompile |
| MSI Decompiler | Implemented | MsiDecompiler with 9 table readers + CSharpEmitter |
| Bundle detach/reattach (for signing) | Missing | |
| PDB debug info | Missing | |
| Incremental builds | Missing | |
| Multi-threaded cabinet creation | Implemented | ParallelCabinetBuilder via Parallel.ForEachAsync |

## 6. Extension Ecosystem

| Extension | Status | Notes |
|-----------|--------|-------|
| Extension framework/interfaces | Implemented | IFalkInstallerExtension, IComponentContributor, IMsiTableContributor |
| Util (CloseApp, Permissions) | Implemented | Full support |
| Util (XmlConfig, User/Group, FileShare) | Implemented | XCF001-009 error codes |
| Util (QuietExec, RemoveFolderEx, InternetShortcut) | Implemented | Full support |
| Util (EventManifest, PerfCounter) | Missing | Niche |
| Firewall rules | Implemented | FirewallRuleBuilder, FWL001-004 |
| IIS (WebSite, AppPool, VirtualDir) | Implemented | 5 builders, IIS001-009 |
| SQL (Database, Script) | Implemented | 3 builders, SQL001-013 |
| .NET detection (DotNetCoreSearch) | Implemented | DotNetDetector, NET001-003 |
| HTTP (URL reservations, SNI SSL) | Missing | |
| Visual Studio (VSIX, detection) | Missing | Niche |
| DirectX (capability detection) | Missing | Niche |
| COM+ (applications, roles) | Missing | Niche |
| Dependency provider/consumer | Missing | |

## 7. Runtime

| Feature | Status | Notes |
|---------|--------|-------|
| Detect/Plan/Apply lifecycle | Implemented | EngineStateMachine, 9 phase handlers |
| Elevated companion (UAC) | Implemented | Engine.Elevation with whitelisted commands |
| Named pipe IPC | Implemented | HMAC-SHA256 handshake, 12 message types |
| Package cache (local) | Implemented | PackageCache, CacheLayout |
| Rollback journal | Implemented | RollbackJournal, RollbackExecutor, 3 undo operations |
| Restart Manager | Implemented | RestartManagerSession, NativeRestartManagerMethods |
| Verbose structured logging | Implemented | EngineLogger, LogEntry |
| Maintenance mode (Modify/Repair) | Implemented | MaintenancePageViewModel, engine + UI flow |
| Feature state migration (upgrades) | Missing | |
| Instance transforms | Missing | Niche |
| Administrative installs | Missing | |

---

## Summary

| Category | Implemented | Partial | Missing | N/A | Coverage |
|----------|:-----------:|:-------:|:-------:|:---:|:--------:|
| Core MSI tables | 28 | 0 | 4 | 0 | ~88% |
| Output types | 5 | 0 | 0 | 1 | 100% |
| MSI UI/Sequences | 5 | 1 | 1 | 0 | ~79% |
| Bundle engine | 16 | 1 | 2 | 1 | ~83% |
| Build system | 6 | 1 | 3 | 2 | ~58% |
| Extensions | 8 | 0 | 4 | 0 | ~67% |
| Runtime | 8 | 0 | 3 | 0 | ~73% |

**Overall estimated coverage: ~75-80% of WiX 6.0.2 functionality.**

## Remaining Gaps (prioritized)

### High Value
- Bundle detach/reattach for signing
- Feature state migration on upgrades
- Dependency provider/consumer extension
- Update feeds for bundle auto-update

### Medium Value
- EmbeddedUI support
- Administrative installs
- Harvesting improvements (full Heat equivalent)
- PDB debug info
- HTTP extension (URL reservations)

### Low Value / Niche
- COM Registration (deferred)
- COM+ extension
- ODBC extension
- Visual Studio extension
- DirectX extension
- IsolateComponent, BindImage, ReserveCost
- Instance transforms
- Incremental builds
- Embedded bundle mode
- EventManifest/PerfCounter (Util)

## Decisions

- **COM Registration:** Explicitly deferred to a future phase.
- **Preprocessor/Fragments/Libraries:** N/A — C# fluent API replaces WiX XML preprocessor and fragment/library model.
- **Themeable standard BA:** N/A — FalkInstaller uses WPF + ReactiveUI for bundle UI, which is more flexible than WiX standard BA themes.
