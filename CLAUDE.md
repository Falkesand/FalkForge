# FalkInstaller

C# MSI/Bundle installer framework. Fluent API for defining packages, MSI compiler via P/Invoke, NativeAOT bundle engine with WPF UI. Extension system for Firewall, IIS, SQL, .NET detection, and utility actions. Supports MSI, MSM, MSP, MST, and EXE bundle output types.

## Build & Test

```bash
dotnet build          # 0 warnings required (TreatWarningsAsErrors)
dotnet test           # ~1380 tests, xUnit 2.9.3
dotnet publish -c Release  # NativeAOT for Engine + Elevation
```

- .NET 10, C# latest, nullable enabled, central package management
- `global.json`: SDK 10.0.103

## Solution Structure (23 src + 14 test projects)

```
src/
  FalkInstaller.Core/                  # Domain model, fluent API, validation
  FalkInstaller.Compiler.Msi/          # MSI/MSM/MSP/MST generation via msi.dll P/Invoke
  FalkInstaller.Compiler.Bundle/       # Self-extracting EXE bundle compiler
  FalkInstaller.Engine/                # NativeAOT installer runtime (exe)
  FalkInstaller.Engine.Elevation/      # NativeAOT elevated companion (exe)
  FalkInstaller.Engine.Protocol/       # IPC message types + serialization (AOT-safe)
  FalkInstaller.Platform/              # OS abstractions (IFileSystem, IRegistry)
  FalkInstaller.Platform.Windows/      # Windows P/Invoke implementations
  FalkInstaller.Extensibility/         # Extension system interfaces
  FalkInstaller.Extensions.Util/       # XmlConfig, UserManagement, FileShare, QuietExec, RemoveFolderEx, InternetShortcut
  FalkInstaller.Extensions.Firewall/   # Firewall rule definitions and validation
  FalkInstaller.Extensions.DotNet/     # .NET runtime detection via registry and filesystem
  FalkInstaller.Extensions.Iis/        # IIS AppPool, WebSite, WebBinding, Certificate configuration
  FalkInstaller.Extensions.Sql/        # SQL Server database, script, and string execution
  FalkInstaller.Ui.Abstractions/       # IInstallerEngine, base ViewModels
  FalkInstaller.Ui/                    # WPF + ReactiveUI installer UI
  FalkInstaller.Sdk/                   # MSBuild SDK targets (netstandard2.0)
  FalkInstaller.Testing/               # Test utilities, mocks

tests/
  FalkInstaller.Core.Tests/
  FalkInstaller.Compiler.Msi.Tests/
  FalkInstaller.Compiler.Bundle.Tests/
  FalkInstaller.Engine.Tests/
  FalkInstaller.Engine.Elevation.Tests/
  FalkInstaller.Engine.Protocol.Tests/
  FalkInstaller.Ui.Abstractions.Tests/
  FalkInstaller.Ui.Tests/
  FalkInstaller.Integration.Tests/
  FalkInstaller.Extensions.Util.Tests/
  FalkInstaller.Extensions.Firewall.Tests/
  FalkInstaller.Extensions.DotNet.Tests/
  FalkInstaller.Extensions.Iis.Tests/
  FalkInstaller.Extensions.Sql.Tests/
```

## Dependency Graph

```
Core (no deps)
  +-> Platform --> Platform.Windows
  +-> Engine.Protocol (AOT-safe) --> Ui.Abstractions --> Ui (WPF+ReactiveUI)
  |                               +-> Compiler.Bundle
  +-> Compiler.Msi (Core + Platform)
  +-> Extensibility (standalone)
  +-> Extensions.Util (Core + Extensibility)
  +-> Extensions.Firewall (Core + Extensibility)
  +-> Extensions.DotNet (Core + Extensibility)
  +-> Extensions.Iis (Core + Extensibility)
  +-> Extensions.Sql (Core + Extensibility)
  +-> Testing (Core + Platform)

Engine (exe):     Engine.Protocol + Platform.Windows + Compiler.Msi
Elevation (exe):  Engine.Protocol + Platform.Windows
```

## Key Patterns & Locations

### Result<T> -- `src/FalkInstaller.Core/Result.cs`
Readonly record struct. `Result<T>.Success(value)` / `Result<T>.Failure(error)`. Match/Map/Bind.

### Error -- `src/FalkInstaller.Core/Error.cs`
`record struct Error(ErrorKind Kind, string Message)`

### ErrorKind -- `src/FalkInstaller.Core/ErrorKind.cs`
29 values: Validation, FileNotFound, CompilationError, SecurityError, ProtocolError, EngineError, ElevationError, BundleError, DownloadError, LayoutError, etc.

### Unit -- `src/FalkInstaller.Core/Unit.cs`
`readonly record struct Unit { static readonly Unit Value = default; }` -- for `Result<Unit>`.

### Entry Point -- `src/FalkInstaller.Core/Installer.cs`
`Installer.Build()` for MSI, `Installer.BuildBundle()` for EXE bundles, `Installer.BuildMergeModule()` for MSM, `Installer.BuildPatch()` for MSP, `Installer.BuildTransform()` for MST.

### ConditionEvaluator -- `src/FalkInstaller.Engine/Variables/ConditionEvaluator.cs`
Recursive-descent parser for WiX-compatible condition expressions (AND, OR, NOT, comparisons, version ranges).

### VariableStore -- `src/FalkInstaller.Engine/Variables/VariableStore.cs`
Thread-safe variable storage with 30+ built-in variables (OS version, architecture, paths, etc.).

### IProcessRunner -- `src/FalkInstaller.Engine/Execution/IProcessRunner.cs`
Abstraction for process execution enabling deterministic testing of MSI/MSU/MSP/Bundle executors.

## Core Project Layout

### Models (`src/FalkInstaller.Core/Models/`) -- 48 files
Top-level: `PackageModel`, `FeatureModel`, `ComponentModel`, `FileEntryModel`
Output Types: `MergeModuleModel`, `PatchModel`, `TransformModel`
Services: `ServiceModel`, `ServiceControlModel`, `ServiceDependencyModel`
Registry: `RegistryEntryModel`, `RemoveRegistryModel`, `RemoveRegistryAction`
Files: `MoveFileModel`, `DuplicateFileModel`, `RemoveFileModel`, `CreateFolderModel`
Actions: `CustomActionModel`, `CustomActionType`
Tables: `CustomTableModel`, `CustomTableColumnModel`, `CustomTableColumnType`
Sequences: `SequenceTable`, `SequenceActionModel`, `SequencePosition` (ActionPosition)
UI: `MsiDialogSet`
Upgrade: `MajorUpgradeModel`, `RemoveExistingProductsSchedule`
Other: `ShortcutModel`, `EnvironmentVariableModel`, `AssemblyModel`, `AssemblyType`, `MediaTemplateModel`, `FeatureConditionModel`, `SigningOptions`, `ExitCodeBehavior`, `RelatedBundleRelation`

### Builders (`src/FalkInstaller.Core/Builders/`) -- 32 files
Main: `PackageBuilder` (orchestrates all sub-builders)
Output Types: `MergeModuleBuilder`, `PatchBuilder`, `TransformBuilder`
Features: `FeatureBuilder`
Files: `FileSetBuilder`, `MoveFileBuilder`, `DuplicateFileBuilder`, `RemoveFileBuilder`, `CreateFolderBuilder`
Services: `ServiceBuilder`, `ServiceControlBuilder`
Registry: `RegistryBuilder`, `RemoveRegistryBuilder`
Actions: `CustomActionBuilder`
Tables: `CustomTableBuilder`, `ColumnOptions`, `RowBuilder`
Sequences: `SequenceBuilder`
Other: `ShortcutBuilder`, `EnvironmentVariableBuilder`, `AssemblyBuilder`, `MajorUpgradeBuilder`, `MediaTemplateBuilder`

### Validation (`src/FalkInstaller.Core/Validation/`)
Static `Validate(PackageModel)` returns `ValidationResult`. Error codes: PKG001, FEA001, SVC001, REG001, CTB001-010, MUP001-003, etc.
Additional validators: `MergeModuleValidator` (MSM001-004), `PatchValidator` (MSP001-004), `TransformValidator` (MST001-002).

## Compiler.Msi Layout

- `MsiCompiler.cs` -- Main MSI compiler (implements `ICompiler`)
- `MsmCompiler.cs` -- Merge module (.msm) compiler
- `PatchCompiler.cs` -- Patch (.msp) compiler
- `TransformCompiler.cs` -- Transform (.mst) compiler
- `FileNameSanitizer.cs` -- Shared filename sanitization
- `MsiDatabase.cs` -- MSI database wrapper (open/insert/query/commit)
- `ComponentResolver.cs` -- Component ID resolution
- `CabinetBuilder.cs` -- Cabinet file generation
- `SummaryInfoWriter.cs` -- MSI summary stream
- `Tables/TableEmitter.cs` -- 1466 lines, emits all MSI tables
- `Tables/MsiTableDefinitions.cs` -- Table schema
- `Tables/EnvironmentEncoding.cs` -- Env var encoding
- `Interop/NativeMethods.Msi.cs` -- msi.dll P/Invoke (LibraryImport)
- `Interop/NativeMethods.Cabinet.cs` -- cabinet.dll P/Invoke
- `Interop/MsiDatabaseHandle.cs`, `MsiRecordHandle.cs`, `MsiViewHandle.cs` -- Safe handles
- `Signing/` -- Code signing support
- `Validation/IceValidator.cs` -- ICE validation
- `UI/MsiDialogModel.cs`, `MsiControlModel.cs`, `MsiControlEventModel.cs`, `MsiControlConditionModel.cs` -- MSI dialog models
- `UI/DialogEmitter.cs` -- Emits MSI dialog tables from models
- `UI/IDialogTemplate.cs` -- Dialog template interface
- `UI/MsiDialogSet.cs` -- Dialog set placeholder
- `UI/Templates/` -- MinimalDialogTemplate, InstallDirDialogTemplate, FeatureTreeDialogTemplate, MondoDialogTemplate, AdvancedDialogTemplate

## Engine Architecture (3-process model)

```
[UI Process]           [Engine Process]          [Elevated Engine]
 WPF + ReactiveUI       NativeAOT (~3-5MB)       NativeAOT (elevated)
 Ui.csproj              Engine.csproj             Engine.Elevation.csproj
       |<-- Named Pipe A -->|<-- Named Pipe B ------->|
```

### Engine State Machine (`src/FalkInstaller.Engine/`)
Phases: Initializing -> Detecting -> Planning -> Elevating -> Applying -> Completing -> Shutdown
Error: any -> Failed -> RollingBack -> Shutdown
- `EngineHost.cs` -- Top-level orchestrator
- `EngineStateMachine.cs` -- Phase transitions
- `EngineContext.cs` -- Shared context
- `Phases/IEnginePhaseHandler.cs` + 9 handlers
- `Detection/PackageDetector.cs`, `MsiDetector.cs`
- `Planning/Planner.cs`, `InstallPlan.cs`, `PlanAction.cs`
- `Execution/PackageExecutor.cs`, `MsiExecutor.cs`, `MsuExecutor.cs`, `MspExecutor.cs`, `BundleExecutor.cs`, `ExitCodeMapping.cs`, `ExecutionOutcome.cs`, `IProcessRunner.cs`, `ProcessRunner.cs`
- `Variables/VariableStore.cs`, `BuiltInVariables.cs`, `ConditionEvaluator.cs`, `ConditionLexer.cs`, `ConditionToken.cs`, `TokenType.cs`
- `Download/PayloadDownloader.cs` -- HTTP download with retry + SHA256 verification
- `Layout/LayoutManager.cs`, `LayoutJsonContext.cs`
- `Cache/PackageCache.cs`, `CacheLayout.cs`
- `Journal/RollbackJournal.cs`, `JournalEntry.cs`, `RollbackExecutor.cs`
- `Journal/UndoOperations/` -- IUndoOperation, MsiUninstallOperation, ExeRollbackOperation, CacheCleanupOperation
- `RestartManager/` -- IRestartManager, RestartManagerSession, RestartManagerProcess, NativeRestartManagerMethods
- `Logging/` -- IEngineLogger, EngineLogger, LogEntry, NullLogger

### Engine.Protocol (`src/FalkInstaller.Engine.Protocol/`)
- `Messages/` -- 12 message types (DetectBegin/Complete, PlanBegin/Complete, ApplyBegin/Complete, Progress, Error, PhaseChanged, Cancel, Log, Shutdown, ElevateExecute/Result)
- `Serialization/MessageSerializer.cs`, `MessageDeserializer.cs` -- Binary format: [Version:ushort][Type:ushort][Length:int][Payload]
- `Transport/PipeServer.cs`, `PipeClient.cs` -- Named pipe IPC with HMAC-SHA256 handshake
- `Manifest/InstallerManifest.cs`, `PackageInfo.cs`, `PackageType.cs`, `RelatedBundleEntry.cs`, `RollbackBoundaryInfo.cs`, `ManifestChainItem.cs`, `PackageManifestChainItem.cs`, `RollbackBoundaryManifestChainItem.cs`

### Engine.Elevation (`src/FalkInstaller.Engine.Elevation/`)
- `ElevatedHost.cs` -- Parse args, verify parent PID, HMAC handshake
- `ElevatedCommandExecutor.cs` -- Whitelisted command dispatch
- `Commands/` -- MsiInstallCommand, MsiUninstallCommand, ServiceInstallCommand, RegistryWriteCommand, FileWriteCommand

### UI (`src/FalkInstaller.Ui/`)
- `EngineClient.cs` -- IInstallerEngine over PipeClient
- `ViewModels/` -- DefaultShellViewModel, WelcomePageViewModel, LicensePageViewModel, InstallDirPageViewModel, FeaturesPageViewModel, ProgressPageViewModel, CompletePageViewModel, MaintenancePageViewModel
- `Views/` -- 8 XAML files (+ MaintenancePage.xaml, MaintenancePage.xaml.cs)
- `Converters/` -- WPF value converters

## Compiler.Bundle Layout

- `Builders/BundleBuilder.cs`, `ChainBuilder.cs`, `BundlePackageBuilder.cs`, `ContainerBuilder.cs`, `RelatedBundleBuilder.cs`, `RollbackBoundaryBuilder.cs`, `MsuPackageBuilder.cs`, `MspPackageBuilder.cs`, `NestedBundlePackageBuilder.cs`
- `Models/ContainerModel.cs`, `RemotePayloadModel.cs`, `RelatedBundleModel.cs`, `RollbackBoundaryModel.cs`, `ChainItem.cs`, `PackageChainItem.cs`, `RollbackBoundaryChainItem.cs`
- `Compilation/BundleCompiler.cs`, `PayloadEmbedder.cs`, `ManifestGenerator.cs`
- `Compression/GzipCompressor.cs`
- `Validation/BundleValidator.cs` -- BDL001-005
- EXE format: [PE stub][Magic: "FALKBUNDLE"][Manifest][Compressed payloads][TOC][Footer]

## NativeAOT Constraints (Engine + Elevation)
- No reflection, no dynamic, no BinaryFormatter
- Manual DI (constructor injection)
- `PublishAot: true`, `InvariantGlobalization: true`, `IlcOptimizationPreference: Size`
- All serialization via MessageSerializer (binary protocol)

## Extension System (`src/FalkInstaller.Extensibility/`)
- `IFalkInstallerExtension` -- Extension entry point (`Name` property + `Register()` method)
- `IComponentContributor`, `IMsiTableContributor` -- Contribute components/tables
- `IExtensionValidator` -- Validate extensions
- `ExtensionContext`, `MsiTableRow`

## Extensions

### Extensions.Util (`src/FalkInstaller.Extensions.Util/`)
XML configuration, user/group management, file shares, quiet execution, folder removal, internet shortcuts.
- Error codes: XCF001-009

### Extensions.Firewall (`src/FalkInstaller.Extensions.Firewall/`)
Windows Firewall rule definitions and validation.
- Error codes: FWL001-004

### Extensions.DotNet (`src/FalkInstaller.Extensions.DotNet/`)
.NET runtime detection via registry and filesystem probing.
- Error codes: NET001-003

### Extensions.Iis (`src/FalkInstaller.Extensions.Iis/`)
IIS application pool, website, web binding, and certificate configuration.
- Error codes: IIS001-009

### Extensions.Sql (`src/FalkInstaller.Extensions.Sql/`)
SQL Server database creation, script execution, and string execution.
- Error codes: SQL001-013

## Namespace Conventions
```
FalkInstaller                              Core types (Result, Error, Unit, Installer)
FalkInstaller.Models                       Domain models
FalkInstaller.Builders                     Fluent builders
FalkInstaller.Validation                   Model validation
FalkInstaller.Compiler.Msi                 MSI compiler
FalkInstaller.Compiler.Msi.Interop         P/Invoke wrappers
FalkInstaller.Compiler.Msi.Tables          Table emitters
FalkInstaller.Compiler.Bundle              Bundle compiler
FalkInstaller.Compiler.Msi.UI              MSI dialog models + emitter
FalkInstaller.Compiler.Msi.UI.Templates   Built-in dialog templates
FalkInstaller.Engine                       Engine runtime
FalkInstaller.Engine.Journal.UndoOperations Rollback undo operations
FalkInstaller.Engine.RestartManager        Restart Manager integration
FalkInstaller.Engine.Logging               Engine logging infrastructure
FalkInstaller.Engine.Phases                State machine phases
FalkInstaller.Engine.Protocol              IPC protocol
FalkInstaller.Engine.Protocol.Transport    Named pipe transport
FalkInstaller.Engine.Elevation             Elevated process
FalkInstaller.Platform                     Platform abstractions
FalkInstaller.Platform.Windows             Windows implementations
FalkInstaller.Ui                           WPF UI
FalkInstaller.Ui.ViewModels                ViewModels
FalkInstaller.Extensions.Util              Utility extension (XmlConfig, UserManagement, etc.)
FalkInstaller.Extensions.Firewall          Firewall extension
FalkInstaller.Extensions.DotNet            .NET detection extension
FalkInstaller.Extensions.Iis               IIS extension
FalkInstaller.Extensions.Sql               SQL Server extension
```
