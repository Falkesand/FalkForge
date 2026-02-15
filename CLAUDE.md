# FalkInstaller

C# MSI/Bundle installer framework. Fluent API for defining packages, MSI compiler via P/Invoke, NativeAOT bundle engine with WPF UI.

## Build & Test

```bash
dotnet build          # 0 warnings required (TreatWarningsAsErrors)
dotnet test           # ~965 tests, xUnit 2.9.3
dotnet publish -c Release  # NativeAOT for Engine + Elevation
```

- .NET 10, C# latest, nullable enabled, central package management
- `global.json`: SDK 10.0.103

## Solution Structure (14 src + 9 test projects)

```
src/
  FalkInstaller.Core/                  # Domain model, fluent API, validation
  FalkInstaller.Compiler.Msi/          # MSI generation via msi.dll P/Invoke
  FalkInstaller.Compiler.Bundle/       # Self-extracting EXE bundle compiler
  FalkInstaller.Engine/                # NativeAOT installer runtime (exe)
  FalkInstaller.Engine.Elevation/      # NativeAOT elevated companion (exe)
  FalkInstaller.Engine.Protocol/       # IPC message types + serialization (AOT-safe)
  FalkInstaller.Platform/              # OS abstractions (IFileSystem, IRegistry)
  FalkInstaller.Platform.Windows/      # Windows P/Invoke implementations
  FalkInstaller.Extensibility/         # Extension system interfaces
  FalkInstaller.Ui.Abstractions/       # IInstallerEngine, base ViewModels
  FalkInstaller.Ui/                    # WPF + ReactiveUI installer UI
  FalkInstaller.Sdk/                   # MSBuild SDK targets (netstandard2.0)
  FalkInstaller.Testing/               # Test utilities, mocks

tests/
  FalkInstaller.Core.Tests/            # 332 tests
  FalkInstaller.Compiler.Msi.Tests/    # 78 tests
  FalkInstaller.Compiler.Bundle.Tests/ # 104 tests
  FalkInstaller.Engine.Tests/          # 270 tests
  FalkInstaller.Engine.Elevation.Tests/# 11 tests
  FalkInstaller.Engine.Protocol.Tests/ # 87 tests
  FalkInstaller.Ui.Abstractions.Tests/ # 42 tests
  FalkInstaller.Ui.Tests/             # 18 tests
  FalkInstaller.Integration.Tests/     # 23 tests
```

## Dependency Graph

```
Core (no deps)
  +-> Platform --> Platform.Windows
  +-> Engine.Protocol (AOT-safe) --> Ui.Abstractions --> Ui (WPF+ReactiveUI)
  |                               +-> Compiler.Bundle
  +-> Compiler.Msi (Core + Platform)
  +-> Extensibility (standalone)
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
`Installer.Build()` for MSI, `Installer.BuildBundle()` for EXE bundles.

### ConditionEvaluator -- `src/FalkInstaller.Engine/Variables/ConditionEvaluator.cs`
Recursive-descent parser for WiX-compatible condition expressions (AND, OR, NOT, comparisons, version ranges).

### VariableStore -- `src/FalkInstaller.Engine/Variables/VariableStore.cs`
Thread-safe variable storage with 30+ built-in variables (OS version, architecture, paths, etc.).

### IProcessRunner -- `src/FalkInstaller.Engine/Execution/IProcessRunner.cs`
Abstraction for process execution enabling deterministic testing of MSI/MSU/MSP/Bundle executors.

## Core Project Layout

### Models (`src/FalkInstaller.Core/Models/`) -- 41 files
Top-level: `PackageModel`, `FeatureModel`, `ComponentModel`, `FileEntryModel`
Services: `ServiceModel`, `ServiceControlModel`, `ServiceDependencyModel`
Registry: `RegistryEntryModel`, `RemoveRegistryModel`, `RemoveRegistryAction`
Files: `MoveFileModel`, `DuplicateFileModel`, `RemoveFileModel`, `CreateFolderModel`
Actions: `CustomActionModel`, `CustomActionType`
Tables: `CustomTableModel`, `CustomTableColumnModel`, `CustomTableColumnType`
Upgrade: `MajorUpgradeModel`, `RemoveExistingProductsSchedule`
Other: `ShortcutModel`, `EnvironmentVariableModel`, `AssemblyModel`, `AssemblyType`, `MediaTemplateModel`, `FeatureConditionModel`, `SigningOptions`, `ExitCodeBehavior`, `RelatedBundleRelation`

### Builders (`src/FalkInstaller.Core/Builders/`) -- 28 files
Main: `PackageBuilder` (orchestrates all sub-builders)
Features: `FeatureBuilder`
Files: `FileSetBuilder`, `MoveFileBuilder`, `DuplicateFileBuilder`, `RemoveFileBuilder`, `CreateFolderBuilder`
Services: `ServiceBuilder`, `ServiceControlBuilder`
Registry: `RegistryBuilder`, `RemoveRegistryBuilder`
Actions: `CustomActionBuilder`
Tables: `CustomTableBuilder`, `ColumnOptions`, `RowBuilder`
Other: `ShortcutBuilder`, `EnvironmentVariableBuilder`, `AssemblyBuilder`, `MajorUpgradeBuilder`, `MediaTemplateBuilder`

### Validation (`src/FalkInstaller.Core/Validation/ModelValidator.cs`)
Static `Validate(PackageModel)` returns `ValidationResult`. Error codes: PKG001, FEA001, SVC001, REG001, CTB001-010, MUP001-003, etc.

## Compiler.Msi Layout

- `MsiCompiler.cs` -- Main compiler (implements `ICompiler`)
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
- `Journal/RollbackJournal.cs`, `JournalEntry.cs`

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
- `ViewModels/` -- DefaultShellViewModel, WelcomePageViewModel, LicensePageViewModel, InstallDirPageViewModel, FeaturesPageViewModel, ProgressPageViewModel, CompletePageViewModel
- `Views/` -- 7 XAML files
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
- `IFalkInstallerExtension` -- Extension entry point
- `IComponentContributor`, `IMsiTableContributor` -- Contribute components/tables
- `IExtensionValidator` -- Validate extensions
- `ExtensionContext`, `MsiTableRow`

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
FalkInstaller.Engine                       Engine runtime
FalkInstaller.Engine.Phases                State machine phases
FalkInstaller.Engine.Protocol              IPC protocol
FalkInstaller.Engine.Protocol.Transport    Named pipe transport
FalkInstaller.Engine.Elevation             Elevated process
FalkInstaller.Platform                     Platform abstractions
FalkInstaller.Platform.Windows             Windows implementations
FalkInstaller.Ui                           WPF UI
FalkInstaller.Ui.ViewModels                ViewModels
```
