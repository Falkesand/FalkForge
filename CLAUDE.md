# FalkForge

C# MSI/Bundle installer framework. Fluent API for defining packages, MSI compiler via P/Invoke, NativeAOT bundle engine with WPF UI. Extension system for Firewall, IIS, SQL, .NET detection, and utility actions. Supports MSI, MSM, MSP, MST, and EXE bundle output types.

## Build & Test

```bash
dotnet build          # 0 warnings required (TreatWarningsAsErrors)
dotnet test           # ~1625 tests, xUnit 2.9.3
dotnet publish -c Release  # NativeAOT for Engine + Elevation
```

- .NET 10, C# latest, nullable enabled, central package management
- `global.json`: SDK 10.0.103

## Solution Structure (21 src + 17 test projects)

```
src/
  FalkForge.Core/                  # Domain model, fluent API, validation
  FalkForge.Compiler.Msi/          # MSI/MSM/MSP/MST generation via msi.dll P/Invoke
  FalkForge.Compiler.Bundle/       # Self-extracting EXE bundle compiler
  FalkForge.Engine/                # NativeAOT installer runtime (exe)
  FalkForge.Engine.Elevation/      # NativeAOT elevated companion (exe)
  FalkForge.Engine.Protocol/       # IPC message types + serialization (AOT-safe)
  FalkForge.Platform/              # OS abstractions (IFileSystem, IRegistry)
  FalkForge.Platform.Windows/      # Windows P/Invoke implementations
  FalkForge.Extensibility/         # Extension system interfaces
  FalkForge.Extensions.Util/       # XmlConfig, UserManagement, FileShare, QuietExec, RemoveFolderEx, InternetShortcut
  FalkForge.Extensions.Firewall/   # Firewall rule definitions and validation
  FalkForge.Extensions.DotNet/     # .NET runtime detection via registry and filesystem
  FalkForge.Extensions.Iis/        # IIS AppPool, WebSite, WebBinding, Certificate configuration
  FalkForge.Extensions.Sql/        # SQL Server database, script, and string execution
  FalkForge.Ui.Abstractions/       # IInstallerEngine, base ViewModels
  FalkForge.Ui/                    # WPF + ReactiveUI installer UI
  FalkForge.Sdk/                   # MSBuild SDK targets (netstandard2.0)
  FalkForge.Testing/               # Test utilities, mocks
  FalkForge.Localization/          # JSON-based localization with culture fallback
  FalkForge.Decompiler/            # MSI decompiler (Windows-only) -> PackageModel + C# source
  FalkForge.Cli/                   # Spectre.Console CLI: build, validate, inspect, decompile

tests/
  FalkForge.Core.Tests/
  FalkForge.Compiler.Msi.Tests/
  FalkForge.Compiler.Bundle.Tests/
  FalkForge.Engine.Tests/
  FalkForge.Engine.Elevation.Tests/
  FalkForge.Engine.Protocol.Tests/
  FalkForge.Ui.Abstractions.Tests/
  FalkForge.Ui.Tests/
  FalkForge.Integration.Tests/
  FalkForge.Extensions.Util.Tests/
  FalkForge.Extensions.Firewall.Tests/
  FalkForge.Extensions.DotNet.Tests/
  FalkForge.Extensions.Iis.Tests/
  FalkForge.Extensions.Sql.Tests/
  FalkForge.Localization.Tests/
  FalkForge.Decompiler.Tests/
  FalkForge.Cli.Tests/
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
  +-> Localization (Core)
  +-> Decompiler (Core + Compiler.Msi, Windows-only)

Engine (exe):     Engine.Protocol + Platform.Windows + Compiler.Msi
Elevation (exe):  Engine.Protocol + Platform.Windows
Cli (exe):        Core + Compiler.Msi + Compiler.Bundle + Decompiler + Localization + Extensibility + Extensions.*
```

## Key Patterns & Locations

### Result<T> -- `src/FalkForge.Core/Result.cs`
Readonly record struct. `Result<T>.Success(value)` / `Result<T>.Failure(error)`. Match/Map/Bind.

### Error -- `src/FalkForge.Core/Error.cs`
`record struct Error(ErrorKind Kind, string Message)`

### ErrorKind -- `src/FalkForge.Core/ErrorKind.cs`
29 values: Validation, FileNotFound, CompilationError, SecurityError, ProtocolError, EngineError, ElevationError, BundleError, DownloadError, LayoutError, etc.

### Unit -- `src/FalkForge.Core/Unit.cs`
`readonly record struct Unit { static readonly Unit Value = default; }` -- for `Result<Unit>`.

### Entry Point -- `src/FalkForge.Core/Installer.cs`
`Installer.Build()` for MSI, `Installer.BuildBundle()` for EXE bundles, `Installer.BuildMergeModule()` for MSM, `Installer.BuildPatch()` for MSP, `Installer.BuildTransform()` for MST.

### ConditionEvaluator -- `src/FalkForge.Engine/Variables/ConditionEvaluator.cs`
Recursive-descent parser for WiX-compatible condition expressions (AND, OR, NOT, comparisons, version ranges).

### VariableStore -- `src/FalkForge.Engine/Variables/VariableStore.cs`
Thread-safe variable storage with 30+ built-in variables (OS version, architecture, paths, etc.).

### IProcessRunner -- `src/FalkForge.Engine/Execution/IProcessRunner.cs`
Abstraction for process execution enabling deterministic testing of MSI/MSU/MSP/Bundle executors.

## Core Project Layout

### Models (`src/FalkForge.Core/Models/`) -- 49 files
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
Localization: `LocalizationData`
Other: `ShortcutModel`, `EnvironmentVariableModel`, `AssemblyModel`, `AssemblyType`, `MediaTemplateModel`, `FeatureConditionModel`, `SigningOptions`, `ExitCodeBehavior`, `RelatedBundleRelation`

### Builders (`src/FalkForge.Core/Builders/`) -- 32 files
Main: `PackageBuilder` (orchestrates all sub-builders)
Output Types: `MergeModuleBuilder`, `PatchBuilder`, `TransformBuilder`
Features: `FeatureBuilder`
Files: `FileSetBuilder`, `MoveFileBuilder`, `DuplicateFileBuilder`, `RemoveFileBuilder`, `CreateFolderBuilder`
Services: `ServiceBuilder`, `ServiceControlBuilder`
Registry: `RegistryBuilder`, `RemoveRegistryBuilder`
Actions: `CustomActionBuilder` -- Includes simplified overload `CustomAction(string binaryPath, string entryPoint, Action<CustomActionBuilder>? configure = null)` that auto-registers binary and creates DllFromBinary action
Tables: `CustomTableBuilder`, `ColumnOptions`, `RowBuilder`
Sequences: `SequenceBuilder`
Other: `ShortcutBuilder`, `EnvironmentVariableBuilder`, `AssemblyBuilder`, `MajorUpgradeBuilder`, `MediaTemplateBuilder`

### Validation (`src/FalkForge.Core/Validation/`)
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
- `CabinetBuilder.cs` -- Cabinet file generation (single-threaded)
- `ParallelCabinetBuilder.cs` -- Multi-threaded cabinet creation via Parallel.ForEachAsync
- `CabinetWorkItem.cs`, `CabinetBuildResult.cs` -- Parallel cabinet record structs
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

### Engine State Machine (`src/FalkForge.Engine/`)
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

### Engine.Protocol (`src/FalkForge.Engine.Protocol/`)
- `Messages/` -- 12 message types (DetectBegin/Complete, PlanBegin/Complete, ApplyBegin/Complete, Progress, Error, PhaseChanged, Cancel, Log, Shutdown, ElevateExecute/Result)
- `Serialization/MessageSerializer.cs`, `MessageDeserializer.cs` -- Binary format: [Version:ushort][Type:ushort][Length:int][Payload]
- `Transport/PipeServer.cs`, `PipeClient.cs` -- Named pipe IPC with HMAC-SHA256 handshake
- `Manifest/InstallerManifest.cs`, `PackageInfo.cs`, `PackageType.cs`, `RelatedBundleEntry.cs`, `RollbackBoundaryInfo.cs`, `ManifestChainItem.cs`, `PackageManifestChainItem.cs`, `RollbackBoundaryManifestChainItem.cs`

### Engine.Elevation (`src/FalkForge.Engine.Elevation/`)
- `ElevatedHost.cs` -- Parse args, verify parent PID, HMAC handshake
- `ElevatedCommandExecutor.cs` -- Whitelisted command dispatch
- `Commands/` -- MsiInstallCommand, MsiUninstallCommand, ServiceInstallCommand, RegistryWriteCommand, FileWriteCommand

### UI (`src/FalkForge.Ui/`)
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

## Extension System (`src/FalkForge.Extensibility/`)
- `IFalkForgeExtension` -- Extension entry point (`Name` property + `Register()` method)
- `IComponentContributor`, `IMsiTableContributor` -- Contribute components/tables
- `IExtensionValidator` -- Validate extensions
- `ExtensionContext`, `MsiTableRow`

## Extensions

### Extensions.Util (`src/FalkForge.Extensions.Util/`)
XML configuration, user/group management, file shares, quiet execution, folder removal, internet shortcuts.
- Error codes: XCF001-009

### Extensions.Firewall (`src/FalkForge.Extensions.Firewall/`)
Windows Firewall rule definitions and validation.
- Error codes: FWL001-004

### Extensions.DotNet (`src/FalkForge.Extensions.DotNet/`)
.NET runtime detection via registry and filesystem probing.
- Error codes: NET001-003

### Extensions.Iis (`src/FalkForge.Extensions.Iis/`)
IIS application pool, website, web binding, and certificate configuration. Targets `net10.0` (cross-platform model definitions).
- Error codes: IIS001-009

### Extensions.Sql (`src/FalkForge.Extensions.Sql/`)
SQL Server database creation, script execution, and string execution.
- Error codes: SQL001-013

## Localization (`src/FalkForge.Localization/`)
- `LocalizationModel.cs` -- Parsed string table (Culture + Dictionary<string, string>)
- `LocalizationLoader.cs` -- JSON file loading, culture extraction from filename
- `CultureFallbackChain.cs` -- Builds ordered fallback: specific → parent → default (de-AT → de → en-US)
- `LocalizedStringResolver.cs` -- Resolves `!(loc.StringId)` references with nested/circular detection
- `LocalizationBuilder.cs` -- Fluent API: AddCulture(), DefaultCulture(), AddJsonFile(), Build()
- `PackageBuilderExtensions.cs` -- Extension method bridging Localization → Core PackageBuilder
- Error codes: LOC001-004

## Decompiler (`src/FalkForge.Decompiler/`)
Windows-only (`[SupportedOSPlatform("windows")]`).
- `MsiDecompiler.cs` -- Main entry: Decompile(path) → Result<PackageModel>, DecompileToCSharp(path) → Result<string>
- `IMsiTableAccess.cs` -- Abstraction for MSI database reads (testability)
- `MsiTableAccess.cs` -- Production implementation wrapping MsiDatabase
- `DirectoryResolver.cs` -- MSI directory parent-child resolution, standard directory tokens
- `CSharpEmitter.cs` -- PackageModel → fluent C# source via StringBuilder
- `TableReaders/` -- PropertyTableReader, DirectoryTableReader, ComponentTableReader, FileTableReader, FeatureTableReader, RegistryTableReader, ServiceTableReader, ShortcutTableReader, UpgradeTableReader
- Error codes: DEC001-003

## CLI (`src/FalkForge.Cli/`)
Spectre.Console CLI tool (`forge` command). Supports both C# script and JSON config inputs.
- `forge build installer.csx` -- C# script via Roslyn scripting
- `forge build installer.json` -- JSON config file (auto-detected by `.json` extension)
- `Program.cs` -- CommandApp with build/validate/inspect/decompile commands
- `Commands/BuildCommand.cs` -- Roslyn scripting to compile C# definitions; detects JSON and delegates to JsonConfigLoader
- `Commands/ValidateCommand.cs` -- Validation-only mode
- `Commands/InspectCommand.cs` -- MSI metadata display with tree views (Windows-only)
- `Commands/DecompileCommand.cs` -- Delegates to MsiDecompiler (Windows-only)
- `Settings/` -- BuildSettings, ValidateSettings, InspectSettings, DecompileSettings
- `ExitCodes.cs` -- 0=success, 1=validation, 2=compilation, 3=runtime
- `IConsoleOutput.cs`, `SpectreConsoleOutput.cs` -- Console abstraction for testability
- `ScriptLoader.cs` -- Roslyn scripting for C# project loading
- `MsiInspector.cs`, `MsiInspectionResult.cs` -- MSI metadata extraction
- `JsonConfigLoader.cs` -- Maps JSON config → PackageBuilder → PackageModel. Validates required fields, parses GUIDs/versions/enums. Error codes: JSN001-JSN010
- `Models/` -- 19 DTO files for JSON config deserialization:
  - `InstallerConfig.cs` -- Root config (product, features, files, registry, shortcuts, services, env vars, extensions, ui, launchConditions, majorUpgrade)
  - `ProductConfig.cs` -- Name, manufacturer, version, upgradeCode, platform, installScope, description, comments
  - `FeatureConfig.cs`, `FileConfig.cs`, `RegistryConfig.cs`, `ShortcutConfig.cs`, `ServiceConfig.cs`
  - `EnvironmentVariableConfig.cs`, `LaunchConditionConfig.cs`, `MajorUpgradeConfig.cs`
  - `ExtensionsConfig.cs` -- Aggregates all extension configs
  - `FirewallRuleConfig.cs`, `IisConfig.cs`, `IisAppPoolConfig.cs`, `IisWebSiteConfig.cs`, `IisBindingConfig.cs`
  - `SqlConfig.cs`, `SqlScriptConfig.cs`, `DotNetSearchConfig.cs`
- References: Core, Compiler.Msi, Compiler.Bundle, Decompiler, Localization, Extensibility, and all 5 extension projects (Firewall, IIS, SQL, DotNet, Util)

## SDK (`src/FalkForge.Sdk/`)
MSBuild SDK (netstandard2.0) with source generation for referenced project outputs.
- `Sdk.targets` -- Main MSBuild integration
- `_ComputeFalkArtifactPath` target -- Computes expected artifact path from FalkOutputType (Msi/Msm/Msp/Mst/Bundle)
- `_GetFalkForgeOutput` target -- Exports project output metadata for referencing projects
- `_GenerateProjectOutputs` target -- Generates `ProjectOutputs.g.cs` from ProjectReference items with `ReferenceOutputAssembly=false`
- `_WriteFalkProjectOutputsSource` inline task (RoslynCodeTaskFactory) -- C# code generation with identifier sanitization (char.IsLetterOrDigit allowlist), XML-escaped doc comments, quote-escaped paths
- Generated class: `ProjectOutputs` with static properties for each referenced project's artifact path

## Demos (`demo/`)

### C# Script Demos (10 projects)
- `01-hello-world/` -- Minimal single-file MSI installer
- `02-notepad-clone/` -- Notepad-style app with shortcuts and file associations
- `03-client-server/` -- Multi-component client/server with services
- `04-dev-toolkit/` -- Developer tools with environment variables and registry
- `05-enterprise-suite/` -- Feature tree with multiple optional components
- `06-product-suite/` -- EXE bundle packaging multiple MSI packages
- `07-extensions-showcase/` -- Firewall, IIS, SQL, .NET detection, and utility extensions
- `08-localization/` -- Multi-language installer with culture fallback
- `09-advanced-msi/` -- Custom actions, custom tables, sequence manipulation, merge modules, patches, transforms
- `10-advanced-bundle/` -- Multi-project bundle with rollback boundaries, related bundles, MSU/MSP packages

### JSON Config Demos (`demo/json/`, 7 files)
- `01-minimal.json` -- Minimal JSON-driven MSI
- `02-installdir.json` -- InstallDir dialog set
- `03-featuretree.json` -- Feature tree with multiple components
- `04-mondo.json` -- Mondo dialog set with all features
- `05-advanced.json` -- Advanced dialog set with registry, shortcuts, env vars
- `06-web-server.json` -- IIS web server with firewall rules
- `07-database-app.json` -- SQL Server database deployment
- `payload/` -- Shared dummy payload files for JSON demos

## Namespace Conventions
```
FalkForge                              Core types (Result, Error, Unit, Installer)
FalkForge.Models                       Domain models
FalkForge.Builders                     Fluent builders
FalkForge.Validation                   Model validation
FalkForge.Compiler.Msi                 MSI compiler
FalkForge.Compiler.Msi.Interop         P/Invoke wrappers
FalkForge.Compiler.Msi.Tables          Table emitters
FalkForge.Compiler.Bundle              Bundle compiler
FalkForge.Compiler.Msi.UI              MSI dialog models + emitter
FalkForge.Compiler.Msi.UI.Templates   Built-in dialog templates
FalkForge.Engine                       Engine runtime
FalkForge.Engine.Journal.UndoOperations Rollback undo operations
FalkForge.Engine.RestartManager        Restart Manager integration
FalkForge.Engine.Logging               Engine logging infrastructure
FalkForge.Engine.Phases                State machine phases
FalkForge.Engine.Protocol              IPC protocol
FalkForge.Engine.Protocol.Transport    Named pipe transport
FalkForge.Engine.Elevation             Elevated process
FalkForge.Platform                     Platform abstractions
FalkForge.Platform.Windows             Windows implementations
FalkForge.Ui                           WPF UI
FalkForge.Ui.ViewModels                ViewModels
FalkForge.Extensions.Util              Utility extension (XmlConfig, UserManagement, etc.)
FalkForge.Extensions.Firewall          Firewall extension
FalkForge.Extensions.DotNet            .NET detection extension
FalkForge.Extensions.Iis               IIS extension
FalkForge.Extensions.Sql               SQL Server extension
FalkForge.Localization                 JSON localization + culture fallback
FalkForge.Decompiler                   MSI decompiler (Windows-only)
FalkForge.Decompiler.TableReaders      Per-table MSI readers
FalkForge.Cli                          Spectre.Console CLI tool
FalkForge.Cli.Commands                 CLI command implementations
FalkForge.Cli.Models                   JSON config DTO models (19 files)
FalkForge.Cli.Settings                 CLI command settings
```
