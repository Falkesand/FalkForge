# FalkInstaller Feature Parity Implementation Plan

**Date:** 2026-02-15
**Baseline:** 13 source projects, 9 test projects, 522 tests, 3 phases complete
**Target:** WiX Toolset 6.0.2 feature parity (excluding COM Registration, Preprocessor/Fragments/Libraries)
**Approach:** 5 phases, each independently shippable, ordered by frequency of use and dependency chains

---

## Phase 4 — MSI Authoring Completeness

**Goal:** Cover the remaining MSI table operations that 80%+ of real-world installers need. After this phase, FalkInstaller can author any standard MSI without workarounds.

**Estimated duration:** 4-6 weeks
**Estimated test count increase:** +180 tests (total ~700)

### Tasks

#### 4A. MajorUpgrade Simplified Element (Small)
Wrap the existing `UpgradeBuilder` with a single-call `MajorUpgrade()` method on `PackageBuilder` that sets standard defaults (detect older, block downgrades, schedule `RemoveExistingProducts` after `InstallInitialize`). This is the most common upgrade pattern and currently requires manual `Upgrade()` configuration.

**Files to create:**
- `src/FalkInstaller.Core/Builders/MajorUpgradeBuilder.cs` -- fluent builder with `Schedule`, `AllowDowngrades`, `AllowSameVersion`, `DowngradeErrorMessage`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `MajorUpgrade(Action<MajorUpgradeBuilder>)` method
- `src/FalkInstaller.Core/Models/UpgradeModel.cs` -- add `RemoveExistingProductsSchedule` enum property
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- emit `RemoveExistingProducts` at the configured sequence number

**Tests:** ~8 tests

**Can parallel with:** 4B, 4C, 4D, 4E, 4F, 4G

---

#### 4B. RemoveRegistryKey / RemoveRegistryValue (Small)
Add the `RemoveRegistry` MSI table. This is needed for clean uninstalls that remove registry keys not created by the installer (e.g., application state).

**Files to create:**
- `src/FalkInstaller.Core/Models/RemoveRegistryModel.cs`
- `src/FalkInstaller.Core/Builders/RemoveRegistryBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `RemoveRegistry()` method
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `RemoveRegistryEntries` collection
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateRemoveRegistryTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitRemoveRegistry()`
- `src/FalkInstaller.Core/Validation/ModelValidator.cs` -- validation rules

**Tests:** ~12 tests

**Can parallel with:** 4A, 4C, 4D, 4E, 4F, 4G

---

#### 4C. ServiceControl & ServiceDependency (Medium)
The current `ServiceBuilder` emits `ServiceInstall` and basic `ServiceControl` rows (start on install, stop on uninstall). This task adds explicit `ServiceControl` configuration (stop/start/delete events per install/uninstall/both) and `MsiServiceConfig`/`MsiServiceConfigFailureActions` for dependency chains. The existing `ServiceModel` already has a `Dependencies` list, but `ServiceControl` events are hardcoded.

**Files to create:**
- `src/FalkInstaller.Core/Models/ServiceControlModel.cs` -- events, wait flag, component ref
- `src/FalkInstaller.Core/Builders/ServiceControlBuilder.cs`
- `src/FalkInstaller.Core/Models/ServiceDependencyModel.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `ServiceControl()` method
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `ServiceControls` collection
- `src/FalkInstaller.Core/Builders/ServiceBuilder.cs` -- add `DependsOn()` fluent method for the `MsiServiceConfig` dependency group approach
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateMsiServiceConfigTable`, `CreateMsiServiceConfigFailureActionsTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitServiceControls()`, `EmitMsiServiceConfig()`; refactor existing hardcoded `ServiceControl` rows in `EmitServices()` to honor the new model
- `src/FalkInstaller.Core/Validation/ModelValidator.cs` -- validation rules

**Tests:** ~20 tests

**Can parallel with:** 4A, 4B, 4D, 4E, 4F, 4G

---

#### 4D. File Operations: RemoveFile, RemoveFolder, CreateFolder, MoveFiles, DuplicateFiles, CopyFile (Medium)
Six related MSI tables that manage files beyond simple `InstallFiles`. These are used in upgrade scenarios (removing obsolete files), creating empty directories, and file manipulation during install.

**Files to create:**
- `src/FalkInstaller.Core/Models/RemoveFileModel.cs`
- `src/FalkInstaller.Core/Models/CreateFolderModel.cs`
- `src/FalkInstaller.Core/Models/MoveFileModel.cs`
- `src/FalkInstaller.Core/Models/DuplicateFileModel.cs`
- `src/FalkInstaller.Core/Builders/RemoveFileBuilder.cs`
- `src/FalkInstaller.Core/Builders/CreateFolderBuilder.cs`
- `src/FalkInstaller.Core/Builders/MoveFileBuilder.cs`
- `src/FalkInstaller.Core/Builders/DuplicateFileBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `RemoveFile()`, `RemoveFolder()`, `CreateFolder()`, `MoveFile()`, `DuplicateFile()` methods
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add corresponding collections
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateRemoveFileTable`, `CreateCreateFolderTable`, `CreateMoveFileTable`, `CreateDuplicateFileTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitRemoveFiles()`, `EmitCreateFolders()`, `EmitMoveFiles()`, `EmitDuplicateFiles()`; add `MoveFiles` (3800), `DuplicateFiles` (4010), `RemoveFolders` (3600) to `EmitInstallSequences()`
- `src/FalkInstaller.Core/Validation/ModelValidator.cs` -- validation rules

**Tests:** ~30 tests

**Can parallel with:** 4A, 4B, 4C, 4E, 4F, 4G

---

#### 4E. Conditions on Features and Components (Small)
Add `Condition` property to `FeatureModel` (for the MSI `Condition` table) and pass through the existing `Condition` column on the `Component` table (currently hardcoded to empty string in `EmitComponents`).

**Files to create:**
- `src/FalkInstaller.Core/Models/FeatureConditionModel.cs` -- condition string + level mapping

**Files to modify:**
- `src/FalkInstaller.Core/Models/FeatureModel.cs` -- add `Condition` property
- `src/FalkInstaller.Core/Builders/FeatureBuilder.cs` -- add `Condition()` fluent method
- `src/FalkInstaller.Core/Models/FileEntryModel.cs` -- add `ComponentCondition` property (flows through to component)
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateConditionTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitConditions()` for Feature conditions; modify `EmitComponents()` to use component condition instead of empty string
- `src/FalkInstaller.Compiler.Msi/ComponentResolver.cs` -- propagate `ComponentCondition` from `FileEntryModel` to `ResolvedComponent`
- `src/FalkInstaller.Compiler.Msi/ResolvedComponent.cs` -- add `Condition` property

**Tests:** ~15 tests

**Can parallel with:** 4A, 4B, 4C, 4D, 4F, 4G

---

#### 4F. Custom Action Rollback/Commit Scheduling (Small)
The current `CustomActionType` constants cover deferred, but not rollback (type bit 0x100) or commit (type bit 0x200) scheduling. Also missing: `msidbCustomActionTypeInScript` (0x400), `msidbCustomActionTypeNoImpersonate` (0x800).

**Files to modify:**
- `src/FalkInstaller.Core/Models/CustomActionType.cs` -- add `Rollback`, `Commit`, `InScript`, `NoImpersonate` bit flags and compound constants
- `src/FalkInstaller.Core/Builders/CustomActionBuilder.cs` -- add `Rollback()`, `Commit()`, `Deferred()`, `NoImpersonate()` fluent scheduling methods that set the appropriate type bits
- `src/FalkInstaller.Core/Validation/ModelValidator.cs` -- validate rollback CA requires a matching deferred CA

**Tests:** ~10 tests

**Can parallel with:** 4A, 4B, 4C, 4D, 4E, 4G

---

#### 4G. Custom Tables & MediaTemplate (Small)
Custom tables allow extensions and advanced users to embed arbitrary data in the MSI. `MediaTemplate` auto-generates `Media` table rows for multi-cabinet scenarios.

**Files to create:**
- `src/FalkInstaller.Core/Models/CustomTableModel.cs` -- table name, column definitions, row data
- `src/FalkInstaller.Core/Builders/CustomTableBuilder.cs`
- `src/FalkInstaller.Core/Models/MediaTemplateModel.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `CustomTable()` and `MediaTemplate()` methods
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `CustomTables` collection, `MediaTemplate` property
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitCustomTables()`, modify `EmitMedia()` to support `MediaTemplate` auto-generation
- `src/FalkInstaller.Core/Validation/ModelValidator.cs` -- validate column types, table name length

**Tests:** ~15 tests

**Can parallel with:** 4A, 4B, 4C, 4D, 4E, 4F

---

#### 4H. Assembly GAC Registration (Small)
Emit the `MsiAssembly` and `MsiAssemblyName` tables for .NET Framework assemblies targeting the GAC. Niche but required for parity.

**Files to create:**
- `src/FalkInstaller.Core/Models/AssemblyModel.cs`
- `src/FalkInstaller.Core/Builders/AssemblyBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `GacAssembly()` method
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `Assemblies` collection
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateMsiAssemblyTable`, `CreateMsiAssemblyNameTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- add `EmitAssemblies()`

**Tests:** ~10 tests

**Can parallel with:** all other 4x tasks

---

### Phase 4 Dependencies

```
4A ──┐
4B ──┤
4C ──┤
4D ──┼──→ Phase 4 complete
4E ──┤
4F ──┤
4G ──┤
4H ──┘
(All tasks are independent — maximum parallelism)
```

### What Becomes Possible After Phase 4

- Author MSI packages covering 95%+ of real-world installer needs
- Clean uninstall with registry/file removal
- Conditional installation (install component X only on Windows Server, feature Y only if .NET 8 found)
- Service dependency chains
- Proper upgrade scenarios with file cleanup
- Custom data tables for extension communication
- GAC registration for .NET Framework libraries

---

## Phase 5 — Bundle Engine Intelligence

**Goal:** Transform the bundle engine from a simple package sequencer into an intelligent dependency resolver with conditions, variables, rollback boundaries, and additional package types. This is the difference between a proof-of-concept and a production bootstrapper.

**Estimated duration:** 6-8 weeks
**Estimated test count increase:** +200 tests (total ~900)

### Tasks

#### 5A. Variables & Condition System (Large)
The foundation for all other bundle intelligence. Implements a variable store with 30+ built-in variables (e.g., `VersionNT`, `NativeMachine`, `SystemFolder`, `ProcessorArchitecture`, `Privileged`, `AdminToolsFolder`, `InstalledProductCode_{PackageId}`) and a condition evaluator that supports the WiX condition syntax (`=`, `<>`, `<`, `>`, `>=`, `<=`, `~=`, `AND`, `OR`, `NOT`, parentheses).

**Files to create:**
- `src/FalkInstaller.Engine/Variables/VariableStore.cs` -- dictionary with typed values (string, int, version)
- `src/FalkInstaller.Engine/Variables/BuiltInVariables.cs` -- populates 30+ system variables from platform services
- `src/FalkInstaller.Engine/Variables/ConditionEvaluator.cs` -- recursive-descent parser for condition expressions
- `src/FalkInstaller.Engine/Variables/ConditionToken.cs`
- `src/FalkInstaller.Engine/Variables/ConditionLexer.cs`

**Files to modify:**
- `src/FalkInstaller.Engine/EngineContext.cs` -- add `VariableStore Variables` property
- `src/FalkInstaller.Engine/Phases/InitializingHandler.cs` -- populate built-in variables
- `src/FalkInstaller.Engine/Phases/DetectingHandler.cs` -- set detection variables (installed version, per-package detection results)
- `src/FalkInstaller.Platform/IEnvironment.cs` -- add OS version/arch query methods if not present
- `src/FalkInstaller.Platform.Windows/WindowsEnvironment.cs` -- implement new methods

**Tests:** ~60 tests (condition parser alone needs extensive coverage)

**Depends on:** None
**Can parallel with:** 5B (partially -- 5C-5G depend on 5A)

---

#### 5B. Additional Package Types: MsuPackage, MspPackage, BundlePackage (Medium)
Add Windows Update (.msu), MSI Patch (.msp), and nested Bundle (.exe) package types to the chain.

**Files to create:**
- `src/FalkInstaller.Engine/Execution/MsuExecutor.cs` -- shell-execute wusa.exe with exit code mapping
- `src/FalkInstaller.Engine/Execution/MspExecutor.cs` -- MsiApplyPatch P/Invoke or msiexec /p
- `src/FalkInstaller.Engine/Execution/BundleExecutor.cs` -- launch nested bundle with /quiet and pipe relay
- `src/FalkInstaller.Compiler.Bundle/Builders/MsuPackageBuilder.cs`
- `src/FalkInstaller.Compiler.Bundle/Builders/MspPackageBuilder.cs`
- `src/FalkInstaller.Compiler.Bundle/Builders/NestedBundlePackageBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/BundlePackageType.cs` -- add `MsuPackage`, `MspPackage`, `BundlePackage`
- `src/FalkInstaller.Engine.Protocol/Manifest/PackageType.cs` -- add matching types
- `src/FalkInstaller.Engine/Execution/PackageExecutor.cs` -- route to new executors
- `src/FalkInstaller.Compiler.Bundle/Builders/ChainBuilder.cs` -- add `MsuPackage()`, `MspPackage()`, `BundlePackage()` methods

**Tests:** ~25 tests

**Can parallel with:** 5A

---

#### 5C. Package Conditions & ExitCode Mapping (Medium)
`InstallCondition` determines whether a package should be installed based on evaluated conditions. `ExitCode` mapping translates non-zero process exit codes to success/failure/reboot/schedule-reboot.

**Files to create:**
- `src/FalkInstaller.Engine/Execution/ExitCodeMapping.cs` -- exit code -> result mapping
- `src/FalkInstaller.Core/Models/ExitCodeBehavior.cs` -- `Success`, `Failure`, `RebootRequired`, `ScheduleReboot`

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/BundlePackageModel.cs` -- `InstallCondition` already exists (string); add `ExitCodes` dictionary
- `src/FalkInstaller.Compiler.Bundle/Builders/BundlePackageBuilder.cs` -- add `ExitCode()` fluent method
- `src/FalkInstaller.Engine/Planning/Planner.cs` -- evaluate `InstallCondition` against `VariableStore`; skip packages with false conditions
- `src/FalkInstaller.Engine/Execution/PackageExecutor.cs` -- apply exit code mapping after execution
- `src/FalkInstaller.Engine.Protocol/Manifest/PackageInfo.cs` -- add `InstallCondition`, `ExitCodes`
- `src/FalkInstaller.Compiler.Bundle/Compilation/ManifestGenerator.cs` -- serialize conditions and exit codes

**Tests:** ~25 tests

**Depends on:** 5A (condition evaluator)

---

#### 5D. Rollback Boundaries (Medium)
Divide the package chain into rollback segments. If a package in segment N fails, only packages in segment N are rolled back -- packages in segments 1..N-1 remain installed. Without boundaries, a failure in the last package rolls back everything.

**Files to create:**
- `src/FalkInstaller.Engine/Planning/RollbackBoundary.cs` -- boundary model with vital flag
- `src/FalkInstaller.Compiler.Bundle/Builders/RollbackBoundaryBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/BundleModel.cs` -- packages become a list of `ChainItem` (package or boundary)
- `src/FalkInstaller.Compiler.Bundle/Builders/ChainBuilder.cs` -- add `RollbackBoundary()` method
- `src/FalkInstaller.Engine/Planning/Planner.cs` -- segment the plan into rollback groups
- `src/FalkInstaller.Engine/Planning/InstallPlan.cs` -- add `RollbackSegments` grouping
- `src/FalkInstaller.Engine/Phases/ApplyingHandler.cs` -- on failure, roll back only the current segment
- `src/FalkInstaller.Engine/Phases/RollingBackHandler.cs` -- execute rollback from journal for the failed segment only
- `src/FalkInstaller.Engine/Journal/RollbackJournal.cs` -- add segment markers

**Tests:** ~20 tests

**Depends on:** None (can start after 5A is in progress, integrates at planning level)

---

#### 5E. Related Bundles: Upgrade, Addon, Patch, Detect (Medium)
Detect previously installed bundles with the same `UpgradeCode` and determine the relationship (upgrade replaces, addon supplements, patch patches, detect-only). This drives the engine's decision to uninstall old versions, enable side-by-side, or block conflicting installations.

**Files to create:**
- `src/FalkInstaller.Engine/Detection/RelatedBundleDetector.cs` -- scan ARP registry for matching UpgradeCode
- `src/FalkInstaller.Engine/Detection/RelatedBundleInfo.cs` -- id, version, relationship type
- `src/FalkInstaller.Core/Models/RelatedBundleRelation.cs` -- enum: `Upgrade`, `Addon`, `Patch`, `Detect`

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/BundleModel.cs` -- add `RelatedBundles` with relationship type
- `src/FalkInstaller.Compiler.Bundle/Builders/BundleBuilder.cs` -- add `RelatedBundle()` method
- `src/FalkInstaller.Engine/Detection/PackageDetector.cs` -- call `RelatedBundleDetector`
- `src/FalkInstaller.Engine/Detection/DetectionResult.cs` -- add `RelatedBundles` list
- `src/FalkInstaller.Engine/Planning/Planner.cs` -- plan uninstall of superseded bundles during upgrade
- `src/FalkInstaller.Engine/EngineContext.cs` -- add `RelatedBundles` property
- `src/FalkInstaller.Engine.Protocol/Manifest/InstallerManifest.cs` -- add related bundle metadata

**Tests:** ~20 tests

**Depends on:** None

---

#### 5F. Containers, RemotePayload & Layout Mode (Medium)
Containers group payloads for download/extraction. `RemotePayload` specifies a download URL instead of embedding. Layout mode copies all payloads to a folder for offline installation.

**Files to create:**
- `src/FalkInstaller.Compiler.Bundle/ContainerModel.cs`
- `src/FalkInstaller.Compiler.Bundle/RemotePayloadModel.cs`
- `src/FalkInstaller.Compiler.Bundle/Builders/ContainerBuilder.cs`
- `src/FalkInstaller.Engine/Download/PayloadDownloader.cs` -- HTTP download with retry, hash verification
- `src/FalkInstaller.Engine/Layout/LayoutManager.cs` -- copies payloads to target folder

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/BundlePackageModel.cs` -- add `RemotePayload` (URL, hash, size), `ContainerId`
- `src/FalkInstaller.Compiler.Bundle/Builders/BundlePackageBuilder.cs` -- add `RemotePayload()`, `Container()` methods
- `src/FalkInstaller.Compiler.Bundle/Compilation/BundleCompiler.cs` -- handle remote-only payloads (not embedded)
- `src/FalkInstaller.Compiler.Bundle/Compilation/PayloadEmbedder.cs` -- container-aware embedding
- `src/FalkInstaller.Engine/Phases/ApplyingHandler.cs` -- download remote payloads before execution
- `src/FalkInstaller.Engine/Cache/PackageCache.cs` -- cache downloaded payloads
- `src/FalkInstaller.Engine.Protocol/Manifest/PackageInfo.cs` -- add download URL, expected hash

**Tests:** ~25 tests

**Depends on:** None

---

#### 5G. Engine-UI Bidirectional Control (Small)
The `HandleUiMessageAsync` callback in `EngineHost` is currently a no-op. Wire it to actually process `CancelMessage`, `SetInstallDirectoryMessage`, `SetFeatureSelectionMessage`, `RequestDetectMessage`, `RequestPlanMessage`, and `RequestApplyMessage` from the UI.

**Files to modify:**
- `src/FalkInstaller.Engine/EngineHost.cs` -- implement `HandleUiMessageAsync` dispatch to engine phases
- `src/FalkInstaller.Engine/EngineContext.cs` -- add `UserCancelled` flag, `FeatureSelections` dictionary
- `src/FalkInstaller.Engine/Phases/ApplyingHandler.cs` -- check `UserCancelled` between package executions
- `src/FalkInstaller.Engine/Phases/PlanningHandler.cs` -- use `FeatureSelections` from context
- `src/FalkInstaller.Ui/EngineClient.cs` -- verify all message types are correctly dispatched (minor fixes)

**Tests:** ~15 tests

**Depends on:** None

---

### Phase 5 Dependencies

```
5A ──────────────────→ 5C (condition evaluator)
5B (parallel with 5A) ──┐
5D (parallel) ───────────┤
5E (parallel) ───────────┼──→ Phase 5 complete
5F (parallel) ───────────┤
5G (parallel) ───────────┘
```

### What Becomes Possible After Phase 5

- Conditional package installation (install Visual C++ runtime only if not present)
- Download-on-demand installers (small bootstrapper, large payloads fetched from CDN)
- Offline/layout installers for air-gapped environments
- Upgrade detection and clean migration from prior versions
- Rollback isolation (failure in package 5 does not undo packages 1-3)
- Additional package types: Windows Update packages, MSI patches, nested bundles
- True bidirectional UI-engine control (cancel, feature selection, directory changes)
- Variable-driven conditional logic throughout the bundle

---

## Phase 6 — Runtime Robustness & MSI UI

**Goal:** Make the engine production-grade with real rollback execution, structured logging, and restart management. Add MSI-level UI support for standalone MSI scenarios (no bundle wrapper).

**Estimated duration:** 5-7 weeks
**Estimated test count increase:** +150 tests (total ~1050)

### Tasks

#### 6A. Rollback Execution (Large)
The `RollbackJournal` writes entries but `RollingBackHandler` is a placeholder. Implement actual rollback: read journal entries in reverse, execute undo operations (MsiInstallProduct with REMOVE=ALL for MSI, delete cached files, restore registry).

**Files to create:**
- `src/FalkInstaller.Engine/Journal/RollbackExecutor.cs` -- reads journal, dispatches undo operations
- `src/FalkInstaller.Engine/Journal/UndoOperations/IMsiUndoOperation.cs`
- `src/FalkInstaller.Engine/Journal/UndoOperations/MsiUninstallOperation.cs`
- `src/FalkInstaller.Engine/Journal/UndoOperations/ExeRollbackOperation.cs`
- `src/FalkInstaller.Engine/Journal/UndoOperations/CacheCleanupOperation.cs`

**Files to modify:**
- `src/FalkInstaller.Engine/Phases/RollingBackHandler.cs` -- delegate to `RollbackExecutor`
- `src/FalkInstaller.Engine/Phases/ApplyingHandler.cs` -- write detailed journal entries before each action
- `src/FalkInstaller.Engine/Journal/JournalEntry.cs` -- add `PackageId`, `PackageType`, `CachePath` fields
- `src/FalkInstaller.Engine/Journal/JournalEntryType.cs` -- add `MsiInstalled`, `ExeInstalled`, `PayloadCached`, `RegistryModified`
- `src/FalkInstaller.Engine/Journal/RollbackJournal.cs` -- add segment markers for rollback boundaries

**Tests:** ~35 tests

**Depends on:** Phase 5 (5D rollback boundaries)

---

#### 6B. Restart Manager Integration (Medium)
The `PackageModel.EnableRestartManager` flag sets `MSIRMSHUTDOWN=2` but does not integrate with the Windows Restart Manager API (`RmStartSession`, `RmRegisterResources`, `RmShutdown`, `RmRestart`) at the bundle engine level.

**Files to create:**
- `src/FalkInstaller.Engine/RestartManager/RestartManagerSession.cs` -- P/Invoke wrapper around rstrtmgr.dll
- `src/FalkInstaller.Engine/RestartManager/IRestartManager.cs` -- abstraction for testing
- `src/FalkInstaller.Platform.Windows/WindowsRestartManager.cs` -- Windows implementation

**Files to modify:**
- `src/FalkInstaller.Engine/Phases/ApplyingHandler.cs` -- open RM session before applying, register affected processes, shutdown, restart after apply
- `src/FalkInstaller.Engine/EngineContext.cs` -- add `RestartManagerEnabled` and pending reboot tracking
- `src/FalkInstaller.Platform/IPlatformServices.cs` -- add `IRestartManager` accessor

**Tests:** ~15 tests

**Can parallel with:** 6A, 6C, 6D, 6E

---

#### 6C. Structured Logging (Medium)
Replace ad-hoc `Console.Error.WriteLine` and `LogMessage` with a structured logging system. Every engine operation should produce timestamped, categorized log entries suitable for support diagnostics.

**Files to create:**
- `src/FalkInstaller.Engine/Logging/EngineLogger.cs` -- writes to file + pipes to UI
- `src/FalkInstaller.Engine/Logging/LogEntry.cs` -- timestamp, level, category, message, properties dictionary
- `src/FalkInstaller.Engine/Logging/IEngineLogger.cs` -- abstraction

**Files to modify:**
- `src/FalkInstaller.Engine/EngineHost.cs` -- initialize logger, pass to all phases
- `src/FalkInstaller.Engine/EngineContext.cs` -- add `IEngineLogger Logger` property
- `src/FalkInstaller.Engine/Phases/*.cs` -- all phase handlers use logger instead of direct pipe messages
- `src/FalkInstaller.Engine/Execution/MsiExecutor.cs` -- log MSI execution details
- `src/FalkInstaller.Engine.Protocol/LogLevel.cs` -- add `Verbose`, `Debug` levels

**Tests:** ~20 tests

**Can parallel with:** 6A, 6B, 6D, 6E

---

#### 6D. InstallExecuteSequence / InstallUISequence Custom Scheduling (Medium)
The current `EmitInstallSequences` hardcodes standard action sequence numbers. Allow users to insert custom actions at arbitrary positions in both the `InstallExecuteSequence` and `InstallUISequence` tables, and to re-order standard actions.

**Files to create:**
- `src/FalkInstaller.Core/Models/SequenceActionModel.cs` -- action name, table (Execute/UI), sequence, condition
- `src/FalkInstaller.Core/Builders/SequenceBuilder.cs` -- `After()`, `Before()`, `At()` scheduling methods

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `ExecuteSequence(Action<SequenceBuilder>)`, `UISequence(Action<SequenceBuilder>)` methods
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `ExecuteSequenceActions`, `UISequenceActions` collections
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- merge custom sequence entries with standard entries in `EmitInstallSequences()`; add `EmitUISequence()`

**Tests:** ~20 tests

**Can parallel with:** 6A, 6B, 6C, 6E

---

#### 6E. Pre-built UI Dialog Sets (Large)
Create C# equivalents of `WixUI_Minimal`, `WixUI_InstallDir`, `WixUI_FeatureTree`, `WixUI_Mondo`, and `WixUI_Advanced`. These emit MSI `Dialog`, `Control`, `ControlEvent`, `ControlCondition`, `EventMapping`, `TextStyle`, and `UIText` tables for standalone MSI scenarios where no bundle wrapper is used.

**Files to create:**
- `src/FalkInstaller.Compiler.Msi/UI/MsiDialogModel.cs`
- `src/FalkInstaller.Compiler.Msi/UI/MsiControlModel.cs`
- `src/FalkInstaller.Compiler.Msi/UI/MsiDialogSet.cs` -- enum: `Minimal`, `InstallDir`, `FeatureTree`, `Mondo`, `Advanced`
- `src/FalkInstaller.Compiler.Msi/UI/DialogEmitter.cs` -- emits Dialog/Control/ControlEvent/etc. tables
- `src/FalkInstaller.Compiler.Msi/UI/Templates/MinimalDialogTemplate.cs`
- `src/FalkInstaller.Compiler.Msi/UI/Templates/InstallDirDialogTemplate.cs`
- `src/FalkInstaller.Compiler.Msi/UI/Templates/FeatureTreeDialogTemplate.cs`
- `src/FalkInstaller.Compiler.Msi/UI/Templates/MondoDialogTemplate.cs`
- `src/FalkInstaller.Compiler.Msi/UI/Templates/AdvancedDialogTemplate.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `UseDialogSet(MsiDialogSet)` method
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `DialogSet` property
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add `CreateDialogTable`, `CreateControlTable`, `CreateControlEventTable`, `CreateControlConditionTable`, `CreateEventMappingTable`, `CreateTextStyleTable`, `CreateUITextTable`
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- call `DialogEmitter` if dialog set is configured
- `src/FalkInstaller.Compiler.Msi/MsiCompiler.cs` -- wire dialog emission

**Tests:** ~30 tests (5 dialog sets x 6 validation points)

**Can parallel with:** 6A, 6B, 6C, 6D

---

#### 6F. Maintenance Mode UI: Modify/Repair/Uninstall (Medium)
Complete the maintenance mode flow in the WPF bundle UI. The engine already supports `InstallAction.Modify/Repair`, but the UI has no entry point for detected installations.

**Files to create:**
- `src/FalkInstaller.Ui/ViewModels/MaintenancePageViewModel.cs`
- `src/FalkInstaller.Ui/Views/MaintenancePage.xaml`
- `src/FalkInstaller.Ui/Views/MaintenancePage.xaml.cs`

**Files to modify:**
- `src/FalkInstaller.Ui/ViewModels/DefaultShellViewModel.cs` -- detect installed state and show maintenance page instead of welcome
- `src/FalkInstaller.Ui.Abstractions/ViewModels/InstallerShellViewModel.cs` -- add `IsMaintenanceMode` property
- `src/FalkInstaller.Ui/Views/MainWindow.xaml.cs` -- route to maintenance flow
- `src/FalkInstaller.Ui/App.xaml.cs` -- support maintenance command-line switches

**Tests:** ~10 tests (UI viewmodel tests)

**Can parallel with:** 6A, 6B, 6C, 6D, 6E

---

### Phase 6 Dependencies

```
6A (depends on Phase 5D) ──┐
6B (parallel) ──────────────┤
6C (parallel) ──────────────┼──→ Phase 6 complete
6D (parallel) ──────────────┤
6E (parallel) ──────────────┤
6F (parallel) ──────────────┘
```

### What Becomes Possible After Phase 6

- Production-grade rollback: if installation fails, prior state is restored
- Restart Manager: files in use are gracefully handled
- Full diagnostics: structured log files for support investigations
- Standalone MSI with built-in UI (no bundle required for simple installers)
- Maintenance mode: users can modify, repair, or uninstall from Add/Remove Programs
- Custom sequence scheduling for advanced MSI authoring

---

## Phase 7 — Extensions & Output Types

**Goal:** Build the extension packages that cover common infrastructure (firewall, IIS, .NET detection, XML config, user management) and add merge module / patch / transform output types.

**Estimated duration:** 6-8 weeks
**Estimated test count increase:** +200 tests (total ~1250)

### Tasks

#### 7A. Util Extension: XmlConfig, XmlFile (Medium)
XML configuration file manipulation during install -- the most commonly used WiX extension feature by far.

**Files to create:**
- `src/FalkInstaller.Extensions.Util/XmlConfig/XmlConfigModel.cs` -- xpath, element/attribute/value, action (create/delete/set)
- `src/FalkInstaller.Extensions.Util/XmlConfig/XmlConfigBuilder.cs`
- `src/FalkInstaller.Extensions.Util/XmlConfig/XmlConfigTableContributor.cs` -- implements `IMsiTableContributor`
- `src/FalkInstaller.Extensions.Util/XmlConfig/XmlConfigCustomAction.cs` -- deferred CA that applies XPath modifications
- `src/FalkInstaller.Extensions.Util/UtilExtension.cs` -- implements `IFalkInstallerExtension`

**New test project:**
- `tests/FalkInstaller.Extensions.Util.Tests/`

**Tests:** ~25 tests

**Can parallel with:** 7B, 7C, 7D, 7E, 7F, 7G

---

#### 7B. Util Extension: User/Group, FileShare, QuietExec, RemoveFolderEx (Medium)
Common administrative operations: create Windows users/groups, create file shares, run silent commands, and recursive folder removal on uninstall.

**Files to create:**
- `src/FalkInstaller.Extensions.Util/UserManagement/UserModel.cs`
- `src/FalkInstaller.Extensions.Util/UserManagement/GroupModel.cs`
- `src/FalkInstaller.Extensions.Util/UserManagement/UserBuilder.cs`
- `src/FalkInstaller.Extensions.Util/FileShare/FileShareModel.cs`
- `src/FalkInstaller.Extensions.Util/FileShare/FileShareBuilder.cs`
- `src/FalkInstaller.Extensions.Util/QuietExec/QuietExecBuilder.cs`
- `src/FalkInstaller.Extensions.Util/RemoveFolderEx/RemoveFolderExBuilder.cs`
- `src/FalkInstaller.Extensions.Util/InternetShortcut/InternetShortcutBuilder.cs`

**Files to modify:**
- `src/FalkInstaller.Extensions.Util/UtilExtension.cs` -- register all contributors

**Tests:** ~30 tests

**Can parallel with:** 7A, 7C, 7D, 7E, 7F, 7G

---

#### 7C. Firewall Extension (Medium)
Add/remove Windows Firewall rules during install. One of the most requested WiX extension features for server applications.

**Files to create:**
- `src/FalkInstaller.Extensions.Firewall/FirewallRuleModel.cs` -- name, protocol, port, program, profile, direction
- `src/FalkInstaller.Extensions.Firewall/FirewallRuleBuilder.cs`
- `src/FalkInstaller.Extensions.Firewall/FirewallExtension.cs`
- `src/FalkInstaller.Extensions.Firewall/FirewallCustomAction.cs` -- deferred CA using `INetFwPolicy2` COM interface
- `tests/FalkInstaller.Extensions.Firewall.Tests/`

**Tests:** ~15 tests

**Can parallel with:** 7A, 7B, 7D, 7E, 7F, 7G

---

#### 7D. .NET Detection Extension (Small)
Search for installed .NET Core/.NET 5+ runtimes, SDKs, and hosting bundles. Populates bundle variables for condition evaluation.

**Files to create:**
- `src/FalkInstaller.Extensions.DotNet/DotNetCoreSearchModel.cs` -- runtime type, platform, min version
- `src/FalkInstaller.Extensions.DotNet/DotNetCoreSearchBuilder.cs`
- `src/FalkInstaller.Extensions.DotNet/DotNetDetector.cs` -- registry + hostfxr.dll probing
- `src/FalkInstaller.Extensions.DotNet/DotNetExtension.cs`
- `tests/FalkInstaller.Extensions.DotNet.Tests/`

**Tests:** ~15 tests

**Can parallel with:** 7A, 7B, 7C, 7E, 7F, 7G

---

#### 7E. IIS Extension (Large)
Create/configure IIS web sites, application pools, virtual directories, and SSL certificates. The most complex WiX extension.

**Files to create:**
- `src/FalkInstaller.Extensions.Iis/Models/WebSiteModel.cs`
- `src/FalkInstaller.Extensions.Iis/Models/AppPoolModel.cs`
- `src/FalkInstaller.Extensions.Iis/Models/VirtualDirectoryModel.cs`
- `src/FalkInstaller.Extensions.Iis/Models/CertificateModel.cs`
- `src/FalkInstaller.Extensions.Iis/Builders/WebSiteBuilder.cs`
- `src/FalkInstaller.Extensions.Iis/Builders/AppPoolBuilder.cs`
- `src/FalkInstaller.Extensions.Iis/IisExtension.cs`
- `src/FalkInstaller.Extensions.Iis/IisCustomAction.cs` -- deferred CA using `Microsoft.Web.Administration`
- `tests/FalkInstaller.Extensions.Iis.Tests/`

**Tests:** ~30 tests

**Can parallel with:** 7A, 7B, 7C, 7D, 7F, 7G

---

#### 7F. SQL Extension (Medium)
Create databases, execute SQL scripts, and manage database connections during installation.

**Files to create:**
- `src/FalkInstaller.Extensions.Sql/Models/SqlDatabaseModel.cs`
- `src/FalkInstaller.Extensions.Sql/Models/SqlScriptModel.cs`
- `src/FalkInstaller.Extensions.Sql/Builders/SqlDatabaseBuilder.cs`
- `src/FalkInstaller.Extensions.Sql/SqlExtension.cs`
- `src/FalkInstaller.Extensions.Sql/SqlCustomAction.cs` -- deferred CA for script execution
- `tests/FalkInstaller.Extensions.Sql.Tests/`

**Tests:** ~20 tests

**Can parallel with:** 7A, 7B, 7C, 7D, 7E, 7G

---

#### 7G. Merge Module, Patch, Transform Output Types (Large)
Add `.msm`, `.msp`, and `.mst` output capabilities. Merge modules are reusable component packages. Patches are delta updates. Transforms modify existing MSI databases.

**Files to create:**
- `src/FalkInstaller.Compiler.Msi/MsmCompiler.cs` -- merge module compiler (subset of MsiCompiler with ModuleSignature table)
- `src/FalkInstaller.Compiler.Msi/PatchCompiler.cs` -- generates .msp from old/new MSI pair using MsiCreatePatchFile
- `src/FalkInstaller.Compiler.Msi/TransformCompiler.cs` -- generates .mst from MsiDatabaseGenerateTransform
- `src/FalkInstaller.Core/Builders/MergeModuleBuilder.cs`
- `src/FalkInstaller.Core/Builders/PatchBuilder.cs`
- `src/FalkInstaller.Core/Builders/TransformBuilder.cs`
- `src/FalkInstaller.Core/Models/MergeModuleModel.cs`
- `src/FalkInstaller.Core/Models/PatchModel.cs`
- `src/FalkInstaller.Core/Models/TransformModel.cs`

**Files to modify:**
- `src/FalkInstaller.Core/Installer.cs` -- add `BuildMergeModule()`, `BuildPatch()`, `BuildTransform()` entry points
- `src/FalkInstaller.Core/ICompiler.cs` -- add overloads or separate interfaces for each output type

**Tests:** ~25 tests

**Can parallel with:** 7A-7F

---

### Phase 7 Dependencies

```
7A ──┐
7B ──┤
7C ──┤
7D ──┼──→ Phase 7 complete
7E ──┤
7F ──┤
7G ──┘
(All tasks are independent — maximum parallelism)
```

### What Becomes Possible After Phase 7

- XML config file manipulation during install (web.config, appsettings.json transforms)
- Windows user/group creation for service accounts
- Firewall rule management for server applications
- .NET runtime prerequisite detection and conditional installation
- IIS web site provisioning for ASP.NET applications
- SQL Server database setup and migration scripts
- Merge modules for redistributable component libraries
- Patch creation for incremental updates
- MST transforms for enterprise deployment customization

---

## Phase 8 — Build System, CLI & Polish

**Goal:** Developer experience, tooling, and the remaining niche features. After this phase, FalkInstaller reaches full WiX 6.0.2 parity.

**Estimated duration:** 4-6 weeks
**Estimated test count increase:** +120 tests (total ~1370)

### Tasks

#### 8A. Localization Support (Medium)
Localize MSI string tables, UI text, and bundle UI. WiX uses `.wxl` files; FalkInstaller should use `.resx` or a custom JSON/YAML format that maps culture codes to string tables.

**Files to create:**
- `src/FalkInstaller.Core/Localization/LocalizationModel.cs` -- culture, string dictionary
- `src/FalkInstaller.Core/Localization/LocalizationLoader.cs` -- loads .resx or JSON localization files
- `src/FalkInstaller.Compiler.Msi/Localization/MsiLocalizationEmitter.cs` -- applies localized strings to LOCALIZABLE columns

**Files to modify:**
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add `Localize(string cultureOrPath)` method
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add `Localizations` collection
- `src/FalkInstaller.Compiler.Msi/MsiCompiler.cs` -- apply localizations before commit
- `src/FalkInstaller.Compiler.Msi/SummaryInfoWriter.cs` -- set codepage for target culture

**Tests:** ~20 tests

**Can parallel with:** 8B, 8C, 8D, 8E, 8F

---

#### 8B. CLI Tooling (Medium)
Create `falk` CLI tool with `build`, `validate`, `decompile`, `inspect`, and `format` commands.

**Files to create:**
- New project: `src/FalkInstaller.Cli/` -- .NET tool, NativeAOT
- `src/FalkInstaller.Cli/Program.cs`
- `src/FalkInstaller.Cli/Commands/BuildCommand.cs`
- `src/FalkInstaller.Cli/Commands/ValidateCommand.cs`
- `src/FalkInstaller.Cli/Commands/DecompileCommand.cs`
- `src/FalkInstaller.Cli/Commands/InspectCommand.cs`
- `tests/FalkInstaller.Cli.Tests/`

**Tests:** ~25 tests

**Can parallel with:** 8A, 8C, 8D, 8E, 8F

---

#### 8C. MSI Decompiler (Medium)
Read an existing MSI and produce a `PackageModel` (or C# source code) that would recreate it. Useful for migration from WiX and debugging.

**Files to create:**
- `src/FalkInstaller.Compiler.Msi/Decompiler/MsiDecompiler.cs` -- reads MSI tables, builds PackageModel
- `src/FalkInstaller.Compiler.Msi/Decompiler/TableReader.cs` -- generic MSI table reader
- `src/FalkInstaller.Compiler.Msi/Decompiler/CSharpCodeGenerator.cs` -- emits C# fluent API source

**Tests:** ~20 tests

**Can parallel with:** 8A, 8B, 8D, 8E, 8F

---

#### 8D. Bundle Detach/Reattach for Signing (Small)
Enable signing workflows where the bundle is split into engine stub + payload container, the stub is signed by an HSM, and then reattached.

**Files to create:**
- `src/FalkInstaller.Compiler.Bundle/Signing/BundleDetacher.cs` -- extracts engine from payloads
- `src/FalkInstaller.Compiler.Bundle/Signing/BundleReattacher.cs` -- reattaches after signing

**Files to modify:**
- `src/FalkInstaller.Compiler.Bundle/Compilation/PayloadEmbedder.cs` -- support detach/reattach marker

**Tests:** ~10 tests

**Can parallel with:** 8A, 8B, 8C, 8E, 8F

---

#### 8E. Multi-Threaded Cabinet Creation (Small)
The current `CabinetBuilder` creates a single cabinet sequentially. For large installers (1000+ files), parallel cabinet creation across multiple threads significantly reduces build time.

**Files to modify:**
- `src/FalkInstaller.Compiler.Msi/CabinetBuilder.cs` -- partition files into N groups, build cabinets in parallel using `Parallel.ForEachAsync`, merge via Media table rows
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- update `EmitMedia()` for multi-cabinet support

**Tests:** ~10 tests

**Can parallel with:** 8A, 8B, 8C, 8D, 8F

---

#### 8F. Remaining Extensions: HTTP, Visual Studio, DirectX, Dependency Provider (Medium)
Lower-priority extensions that complete the WiX extension ecosystem parity.

**Files to create:**
- `src/FalkInstaller.Extensions.Http/` -- URL reservation (netsh http), SNI SSL certificate binding
- `src/FalkInstaller.Extensions.VisualStudio/` -- VSIX deployment, VS detection
- `src/FalkInstaller.Extensions.DirectX/` -- GPU/driver capability detection
- `src/FalkInstaller.Extensions.Dependency/` -- dependency provider/consumer for shared component reference counting

**Tests:** ~25 tests

**Can parallel with:** 8A, 8B, 8C, 8D, 8E

---

#### 8G. Niche MSI Features: IsolateComponent, BindImage, ReserveCost, ODBC (Small)
Low-priority features for completeness.

**Files to modify:**
- `src/FalkInstaller.Compiler.Msi/Tables/MsiTableDefinitions.cs` -- add tables
- `src/FalkInstaller.Compiler.Msi/Tables/TableEmitter.cs` -- emit rows
- `src/FalkInstaller.Core/Models/PackageModel.cs` -- add collections
- `src/FalkInstaller.Core/Builders/PackageBuilder.cs` -- add builder methods

**Tests:** ~10 tests

**Can parallel with:** all other 8x tasks

---

### Phase 8 Dependencies

```
8A ──┐
8B ──┤
8C ──┤
8D ──┼──→ Phase 8 complete (Feature parity achieved)
8E ──┤
8F ──┤
8G ──┘
(All tasks are independent — maximum parallelism)
```

### What Becomes Possible After Phase 8

- `falk build` CLI for CI/CD pipelines
- Decompile existing WiX/MSI projects to FalkInstaller C# API
- Localized installers for international distribution
- Signed bundle workflows with HSM support
- Fast builds for large installers via parallel cabinet creation
- Complete WiX extension ecosystem parity
- Full feature parity with WiX Toolset 6.0.2

---

## Summary

| Phase | Name | Tasks | Estimated Tests | Cumulative Tests | Duration |
|:-----:|------|:-----:|:---------------:|:----------------:|:--------:|
| 4 | MSI Authoring Completeness | 8 | +180 | ~700 | 4-6 weeks |
| 5 | Bundle Engine Intelligence | 7 | +200 | ~900 | 6-8 weeks |
| 6 | Runtime Robustness & MSI UI | 6 | +150 | ~1050 | 5-7 weeks |
| 7 | Extensions & Output Types | 7 | +200 | ~1250 | 6-8 weeks |
| 8 | Build System, CLI & Polish | 7 | +120 | ~1370 | 4-6 weeks |
| **Total** | | **35 tasks** | **+850** | **~1370** | **25-35 weeks** |

### Critical Path

```
Phase 4 (all parallel) → Phase 5 (5A unlocks 5C) → Phase 6 (6A depends on 5D) → Phase 7 (all parallel) → Phase 8 (all parallel)
```

Phases 7 and 8 have zero inter-task dependencies and maximum parallelism. Phases 5 and 6 have the tightest dependency chains and represent the critical path. Phase 4 is the fastest to complete due to full parallelism and well-scoped tasks.

### New Project Count

| Phase | New Source Projects | New Test Projects |
|:-----:|:-------------------:|:-----------------:|
| 4 | 0 | 0 |
| 5 | 0 | 0 |
| 6 | 0 | 0 |
| 7 | 5 (Util, Firewall, DotNet, IIS, SQL) | 5 |
| 8 | 2 (CLI, HTTP/VS/DX/Dep extensions) | 2+ |
| **Total** | **+7** | **+7** |

**Final project count: ~20 source projects, ~16 test projects, ~1370 tests.**

### Explicitly Excluded

- **COM Registration (Class/ProgId/TypeLib):** Deferred per project decision. Can be added as a standalone task in any phase.
- **Preprocessor/Fragments/Libraries:** N/A. The C# fluent API replaces these WiX concepts entirely.
- **Embedded UI (MsiSetExternalUIRecord):** Superseded by the bundle-level WPF UI approach. If needed, can be added as a niche feature in Phase 8.
- **Instance Transforms / Administrative Installs / Feature State Migration:** Enterprise-tier features. Can be added as Phase 8 stretch goals if demand warrants.
