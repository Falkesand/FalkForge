# FalkForge

<!-- RULES_SYNCED_FROM_GLOBAL: 2026-06-09 -->

C# MSI/Bundle installer framework. Fluent API, MSI compiler via P/Invoke, NativeAOT bundle engine with WPF UI. Extensions: Firewall, IIS, SQL, .NET, Dependency, Util. Output: MSI, MSM, MSP, MST, EXE bundle.

## Build & Test
```bash
dotnet build          # 0 warnings (TreatWarningsAsErrors)
dotnet test           # ~2484 tests, xUnit 2.9.3
dotnet publish -c Release  # NativeAOT for Engine + Elevation
```
.NET 10, C# latest, nullable enabled, central package mgmt. SDK 10.0.103.

## Solution (26 src + 22 test projects)
| Project | Purpose |
|---------|---------|
| Core | Domain model, fluent API, validation |
| Compiler.Msi | MSI/MSM/MSP/MST generation via msi.dll P/Invoke |
| Compiler.Bundle | Self-extracting EXE bundle compiler |
| Compiler.Msix | **[Experimental]** MSIX/.msixbundle compiler. Studio wired; CLI dispatch not implemented; no demos. Entry point: `InstallerMsix.BuildMsix()` / `BuildMsixBundle()`. |
| Engine | NativeAOT installer runtime (exe) |
| Engine.Elevation | NativeAOT elevated companion (exe) |
| Engine.Protocol | IPC messages + serialization (AOT-safe) |
| Platform / Platform.Windows | OS abstractions / Windows P/Invoke: IFileSystem, IRegistry, IMsiApi (MsiInstallProduct/MsiConfigureProduct). DefaultDllImportSearchPaths(System32) hardening. |
| Extensibility | Extension system interfaces |
| Extensions.Util | XmlConfig, UserMgmt, FileShare, QuietExec, RemoveFolderEx, InternetShortcut |
| Extensions.Dependency | Provider/consumer ref-counting (WiX-compatible) |
| Extensions.Firewall | Firewall rule definitions |
| Extensions.DotNet | .NET runtime detection (registry + filesystem) |
| Extensions.Iis | AppPool, WebSite, WebBinding, Certificate |
| Extensions.Sql | SQL Server DB creation, script/string execution |
| Ui.Abstractions | IInstallerEngine, PageResult, InstallerState, SensitiveBytes |
| Ui | WPF + ReactiveUI installer UI + Custom UI framework |
| Sdk | MSBuild SDK targets (netstandard2.0) |
| Testing | Test utilities, mocks |
| Localization | JSON-based localization with culture fallback |
| Decompiler | MSI (Windows-only) / Bundle (cross-platform) decompiler → PackageModel/BundleModel + C# |
| Cli | Spectre.Console CLI: build, validate, inspect, decompile |
| Plugins.Sql | SQL Server discovery, listing, connection testing |
| Plugins.Odbc | ODBC DSN checking, admin launcher (Windows-only) |
| Plugins.FileSystem | Folder browser dialog (WPF, Windows-only) |

Tests mirror src: `FalkForge.{Project}.Tests/` (21 projects incl. Integration.Tests, Platform.Windows.Tests)

## Dependency Graph
```
Core (no deps)
  ├→ Platform → Platform.Windows
  ├→ Engine.Protocol (AOT-safe) → Ui.Abstractions → Ui (WPF+ReactiveUI)
  │                              └→ Compiler.Bundle
  ├→ Compiler.Msi (Core + Platform)
  ├→ Compiler.Msix (Core + Platform; Windows-only; experimental)
  ├→ Extensibility (standalone)
  ├→ Extensions.* (Core + Extensibility; DotNet also + Platform)
  ├→ Testing (Core + Platform), Localization (Core)
  ├→ Decompiler (Core + Compiler.Msi + Compiler.Bundle)
  └→ Plugins.Sql (Core + SqlClient), Plugins.Odbc/FileSystem (Core, Windows-only)
Engine exe:    Engine.Protocol + Platform.Windows + Compiler.Msi
Elevation exe: Engine.Protocol + Platform.Windows
Cli exe:       Core + Compiler.Msi/Bundle + Decompiler + Localization + Extensibility + Extensions.*
```

## Key Patterns

**Result\<T\>** (`Core/Result.cs`) -- readonly record struct. `.Success(value)` / `.Failure(error)`. Match/Map/Bind.
**Error** (`Core/Error.cs`) -- `record struct Error(ErrorKind Kind, string Message)`. ErrorKind: 29 values (Validation, FileNotFound, CompilationError, SecurityError, ProtocolError, EngineError, ElevationError, BundleError, DownloadError, LayoutError, etc.)
**Unit** (`Core/Unit.cs`) -- `readonly record struct` for `Result<Unit>`.
**Entry Points** (`Core/Installer.cs`) -- `Installer.Build()` MSI, `.BuildBundle()` EXE, `.BuildMergeModule()` MSM, `.BuildPatch()` MSP, `.BuildTransform()` MST.

**WinGet** (`Core/WinGet/`) -- `PackageBuilder.WinGet(w => w.PackageIdentifier("Publisher.App").License("MIT").ShortDescription("..."))`. Auto-generates 3-file WinGet manifest (version + installer + locale YAML) alongside MSI output. SHA-256 computed at compile time. `forge winget <msi>` CLI for existing MSIs.
**Delta Updates** (`Compiler.Bundle/Compilation/DeltaBundleCompiler`) -- `BundleBuilder.DeltaFrom(oldBundlePath)` generates delta bundles using Octodiff. Only changed payload bytes are included. Engine downloads delta-first, falls back to full bundle. TOC uses flag byte for backward compatibility.

**MsiProperty** (`Core/MsiProperty.cs`) -- Type-safe MSI property refs. ~45 built-in statics. `Custom(string)` factory. `/` for path composition. Comparison operators return `Condition`.
**Condition** (`Core/Condition.cs`) -- Type-safe MSI conditions. Pre-composed: Is64BitOS, IsPrivileged, IsAdmin, IsWindows10/11OrLater, IsInstalled/Installing/Uninstalling/Repairing. Operators: `&` AND, `|` OR, `!` NOT. `Property()`, `Raw()` factories. Implicit string conversion.
**Reference Handles** -- Typed cross-refs: `ContainerRef`, `RollbackBoundaryRef` (Compiler.Bundle), `AppPoolRef`, `CertificateRef` (Extensions.Iis), `SqlDatabaseRef` (Extensions.Sql). `Define*` returns ref; consumer methods accept via overloads.

**PageResult** (`Ui.Abstractions/PageResult.cs`) -- Singletons: Next, Previous, Finish, Cancel, Install, Uninstall, Repair. Factories: `Stay(msg?)`, `GoTo<TPage>()`.
**InstallerState** (`Ui.Abstractions/InstallerState.cs`) -- Thread-safe store. `Get<T>/Set<T>`. `SetSensitive/GetSensitive` (DPAPI). IDisposable zeros sensitive data.
**SensitiveBytes** (`Ui.Abstractions/SensitiveBytes.cs`) -- readonly struct wrapping byte[]. IDisposable zeros via CryptographicOperations.ZeroMemory. Always `using`.
**ISensitiveDataProtector** (`Ui.Abstractions/`) → impl `DpapiDataProtector` (`Ui/DpapiDataProtector.cs`) -- Windows DPAPI CurrentUser scope.

**InstallerPage** (`Ui/InstallerPage.cs`) -- Abstract base, internal ctor. Props: Engine, SharedState, DetectedState. Virtual: OnNext/OnBack → PageResult, CanGoNext/Back. `GetPassword(key)` → SensitiveBytes via PasswordBridge. `Localize()` → O(1) lookup. `NotifyCultureChanged()` → blanket WPF refresh.
Lifecycle hooks: `OnDetect/Plan/ApplyBeginAsync()` (return bool, false cancels), `OnDetect/Plan/ApplyCompleteAsync()`. Called by CustomShellViewModel.
**InstallerPage\<TView\>** (`Ui/InstallerPageOfT.cs`) -- Generic subclass, TView : FrameworkElement. Auto-creates view + wires DataContext.
**InstallerApp** (`Ui/InstallerApp.cs`) -- Static `Run(args, configure)` entry point for custom UI.
**PasswordBridge** (`Ui/PasswordBridge.cs`) -- Attached property. XAML: `<PasswordBox ui:PasswordBridge.Key="name"/>`. Page reads via `GetPassword("name")`.

**Plugin System** (`Core/Plugins/`) -- `IInstallerPlugin` (Name + RegisterServices). `IPluginServiceRegistry` (write), `IPluginServices` (read). First-registration-wins, Freeze() locks.
Shipped: Plugins.Sql (`ISqlServerDiscovery, IDatabaseLister, IConnectionTester`), Plugins.Odbc (`IOdbcManager`), Plugins.FileSystem (`IFolderBrowser`).

## Core Layout (`src/FalkForge.Core/`)
**Models** (50+ files): PackageModel, FeatureModel, ComponentModel, FileEntryModel, DirectoryModel, BinaryModel | MergeModuleModel, PatchModel, TransformModel | ServiceModel, ServiceControlModel, ServiceDependencyModel | RegistryEntryModel, RemoveRegistryModel, RemoveRegistryAction | MoveFileModel, DuplicateFileModel, RemoveFileModel, CreateFolderModel | CustomActionModel, CustomActionType | CustomTableModel, CustomTableColumnModel, CustomTableColumnType | SequenceTable, SequenceActionModel, SequencePosition | MajorUpgradeModel, DowngradeModel, RemoveExistingProductsSchedule, UpgradeModel | ShortcutModel, EnvironmentVariableModel, AssemblyModel, AssemblyType, MediaTemplateModel, FeatureConditionModel, SigningOptions, ExitCodeBehavior, RelatedBundleRelation, LaunchConditionModel, PropertyModel, LocalizationData, VerbModel, FontModel, IniFileModel, FileAssociationModel, PermissionModel

**Builders** (34+ files): PackageBuilder (main) | MergeModuleBuilder, PatchBuilder, TransformBuilder | FeatureBuilder | FileSetBuilder, MoveFileBuilder, DuplicateFileBuilder, RemoveFileBuilder, CreateFolderBuilder | ServiceBuilder, ServiceControlBuilder, ServiceFailureActionsBuilder | RegistryBuilder, RegistryKeyBuilder, RemoveRegistryBuilder | CustomActionBuilder (incl. simplified DllFromBinary overload) | CustomTableBuilder, ColumnOptions, RowBuilder | SequenceBuilder | ShortcutBuilder, EnvironmentVariableBuilder, AssemblyBuilder, MajorUpgradeBuilder, DowngradeBuilder, MediaTemplateBuilder, UpgradeBuilder, SigningOptionsBuilder, PropertyBuilder, PermissionBuilder, VerbBuilder, FileAssociationBuilder, FontBuilder, IniFileBuilder

**WinGet/** (`Core/WinGet/`): WinGetManifestWriter (3-file YAML manifest generation), WinGetConfig model. PackageBuilder.WinGet() fluent configuration.

**Validation** (`Core/Validation/`): PKG001-011, FEA001-005, SVC001-008, REG001-007, CTB001-011, MUP001/003, DNG001-002 | MergeModuleValidator MSM001-004 | PatchValidator MSP001-004 | TransformValidator MST001-002

## Compiler.Msi (`src/FalkForge.Compiler.Msi/`)
Compilers: MsiCompiler (ICompiler), MsmCompiler, PatchCompiler, TransformCompiler
DB: MsiDatabase, MsiRecord, FileNameSanitizer, ResolvedPackage/Component/File, ComponentResolver, SummaryInfoWriter
Cabinets: CabinetBuilder (single-threaded), ParallelCabinetBuilder (Parallel.ForEachAsync), CabinetWorkItem, CabinetBuildResult, CabinetExtractor (FDI)
Tables/: MsiTableDefinitions, EnvironmentEncoding
Recipe/: MsiAuthoring, MsiRecipeBuilder, MsiDatabaseRecipe, ITableProducer, IMultiTableProducer, RecipeBuildContext, RecipeTable, RecipeRow, RecipeColumn, CellValue, TableId, TableSchema, ForeignKeySpec, DirectoryTreeSynthesizer
Recipe/Producers/: ComponentTableProducer, DirectoryTableProducer, DialogSetProducer, FeatureTableProducer, FeatureComponentsTableProducer, PropertyTableProducer, RegistryTableProducer, ShortcutTableProducer, ServiceInstallTableProducer, UpgradeTableProducer, and others. ValidateCustomTableIdentifiers() defense-in-depth SQL identifier validation enforced by CustomTablesProducer.
Interop/: NativeMethods.Msi (msi.dll LibraryImport), NativeMethods.Cabinet (cabinet.dll), MsiDatabaseHandle, MsiRecordHandle, MsiViewHandle, FciHandle, FdiHandle. Assembly-level DefaultDllImportSearchPaths(System32) prevents DLL hijacking.
Signing/, Validation/IceValidator
UI/: MsiDialogModel, MsiControlModel, MsiControlEventModel, MsiControlConditionModel, IDialogTemplate. Dialog tables produced via DialogSetProducer in the recipe pipeline.
UI/Templates/: Minimal, InstallDir, FeatureTree, Mondo, Advanced DialogTemplates
BuiltInLocalizationExtensions (`AddBuiltInCultures()`), Localization/en-US.json + sv-SE.json (36 keys each)

## Engine Architecture (3-process model)
```
[UI WPF+ReactiveUI] <--Named Pipe A--> [Engine NativeAOT] <--Named Pipe B--> [Elevated NativeAOT]
```
Phases: Initializing → Detecting → Planning → Elevating → Applying → Completing → Shutdown. Error: any → Failed → RollingBack → Shutdown.

**Engine** (`src/FalkForge.Engine/`):
- EngineHost (dispatches SetProperty/SetSecureProperty to VariableStore; property name validation: regex check, built-in variable blocking (32 names), max length enforcement), EngineStateMachine, EngineContext (UserProperties (ConcurrentDictionary), SecretPropertyNames (ConcurrentDictionary) for property tracking and built-in variable protection)
- Phases/: IEnginePhaseHandler + 9 handlers (Initializing, Detecting, Planning, Elevating, Applying, Completing, RollingBack, Failed, Shutdown)
- Detection/: PackageDetector, MsiDetector, DependencyDetector, DependencyBlocker
- Planning/: Planner, InstallPlan, PlanAction
- Execution/: PackageExecutor, MsiExecutor (uses IMsiApi P/Invoke InstallProduct/ConfigureProduct instead of msiexec.exe; 3-arg ctor with Func<IMsiApi?> lazy accessor; property value injection defense via ProhibitedValueChars), MsuExecutor, MspExecutor, BundleExecutor, ExitCodeMapping, ExecutionOutcome, IProcessRunner, ProcessRunner
- Variables/: VariableStore (30+ built-ins), BuiltInVariables, SecureVariable (IDisposable, zeroed on dispose), ConditionEvaluator (recursive-descent), ConditionLexer, ConditionToken, TokenType
- Download/: PayloadDownloader (HTTP+retry+SHA256), UpdateFeed, UpdateFeedEntry, UpdateInfo, UpdateCheckResult, UpdateFeedJsonContext (AOT), UpdateFeedParser (UPD002-003), DeltaApplicator (Octodiff delta application with SHA-256 verification), UpdateDownloader (delta-first with full-bundle fallback)
- Layout/: LayoutManager, LayoutJsonContext | Cache/: PackageCache, CacheLayout (three-layer path traversal defense: allowlist regex, Path.GetFileName sanitization, Path.GetFullPath containment check)
- Journal/: RollbackJournal, JournalEntry, RollbackExecutor | UndoOperations/: IUndoOperation, MsiUninstallOperation, ExeRollbackOperation, CacheCleanupOperation
- RestartManager/: IRestartManager, RestartManagerSession, RestartManagerProcess, NativeRestartManagerMethods
- Logging/: IEngineLogger, EngineLogger (per-session GUID subdirectory for unpredictable log paths), LogEntry, NullLogger

**Engine.Protocol** (`src/FalkForge.Engine.Protocol/`):
- Messages/ (25 types): Detect/Plan/Apply Begin/Complete, Request Detect/Plan/Apply, Progress, Error, PhaseChanged, Cancel, Log, Shutdown, ElevateExecute/Result, UpdateAvailable/Ready, SetPropertyMessage (0x0208), SetSecurePropertyMessage (0x0209)
- Serialization/: MessageSerializer, MessageDeserializer -- binary [Version:u16][Type:u16][Length:i32][Payload]
- Transport/: PipeServer, PipeClient, PipeConnectionOptions, PipeSecurityValidator -- HMAC-SHA256 handshake
- Manifest/: InstallerManifest (+ IsDeltaUpdate, BaseVersion, BaseBundleSha256), PackageInfo, PackageType, RelatedBundleEntry, RollbackBoundaryInfo, ManifestChainItem, PackageManifestChainItem, RollbackBoundaryManifestChainItem, ManifestDependencyProvider/Consumer, UpdatePolicy (NotifyOnly/DownloadAndPrompt/AutoUpdate), ManifestUpdateFeed. TocEntry gains IsDelta, BaseSha256Hash, ReconstructedSha256Hash for delta payloads. UpdateFeedEntry gains DeltaUrl, DeltaSha256, DeltaSize.

**Engine.Elevation** (`src/FalkForge.Engine.Elevation/`):
ElevatedHost (args, PID verify + PID recycling defense via parent start time capture, HMAC), ElevatedCommandExecutor (whitelisted dispatch), ElevationSecurityLog (file-based security event logger, thread-safe, NativeAOT-compatible), Commands/: MsiInstall, MsiUninstall (both use IMsiApi P/Invoke, not msiexec.exe), ServiceInstall, RegistryWrite, FileWrite

**IInstallerEngine Property Passing**: `SetProperty(name, value)` sends SetPropertyMessage via pipe → EngineHost stores in VariableStore + EngineContext.UserProperties → forwarded to MsiExecutor as `PROPERTY=value`. `SetSecureProperty(name, SensitiveBytes)` sends SetSecurePropertyMessage via pipe → stored as SecureVariable (zeroed on dispose), tracked in EngineContext.SecretPropertyNames → passed to MSI via IMsiApi.SetProperty (never CLI). Use PasswordBridge + GetPassword + SetSecureProperty.

**Compile-Time Security Guards**: REG007 warns when registry values reference MSI properties with sensitive names (PASSWORD, SECRET, CREDENTIAL, TOKEN, APIKEY, PASSPHRASE, PIN). CTB011 does the same for custom table values. These are compile-time warnings — sensitive data written to registry or custom tables is stored in plaintext.

**Self-Extraction**: Bundles support `--extract <dir>` and `--extract-list` command-line switches for extracting embedded payloads without running the installer UI or requiring elevation.

**NativeAOT Constraints**: No reflection/dynamic/BinaryFormatter. Manual DI. PublishAot, InvariantGlobalization, IlcOptimizationPreference=Size. Binary MessageSerializer only.

## UI (`src/FalkForge.Ui/`)
EngineClient (IInstallerEngine over PipeClient), App.xaml (theme + DataTemplates)
Themes/InstallerTheme.xaml -- DynamicResource keys, exterior 164px watermark, interior 59px banner. Override via InstallerWindowBuilder.
ViewModels/: DefaultShellVM, CustomShellVM, Welcome/License/InstallDir/Features/Progress/Complete/MaintenancePageVM
Views/: MainWindow, Welcome/License/InstallDir/Features/Progress/Complete/MaintenancePage, CustomInstallerWindow (.xaml)
Converters/, InstallerUIBuilder, InstallerWindowBuilder, InstallerWindowConfig, PageRegistrar, RelayCommand, NullInstallerEngine
Localization/: UiStringResolver (fallback chain sv-SE→sv→en-US), UiLocalizationBuilder (DefaultCulture/AddJsonResource/DetectCulture/AllowLanguageSelection), UiLocalizationConfig, LanguageSelectorControl

## Compiler.Bundle (`src/FalkForge.Compiler.Bundle/`)
Builders/: BundleBuilder, ChainBuilder, BundlePackageBuilder, ContainerBuilder, RelatedBundleBuilder, RollbackBoundaryBuilder, MsuPackageBuilder, MspPackageBuilder, NestedBundlePackageBuilder
ContainerRef, RollbackBoundaryRef -- typed cross-refs
BundleModel, BundlePackageModel, BundlePackageType, BundleUiConfig (UiType, license, logo, theme, watermark/banner), BundleUiType, UpdateFeedConfig (FeedUrl + Policy)
BundleDependencyProviderModel, BundleDependencyConsumerModel
Models/: ContainerModel, RemotePayloadModel, RelatedBundleModel, RollbackBoundaryModel, ChainItem, PackageChainItem, RollbackBoundaryChainItem
Compilation/: BundleCompiler, ManifestGenerator, ManifestJsonContext, PayloadEmbedder, PayloadEntry, BundleContent, TocEntry
BundleDetacher -- Detach/Reattach for code signing (BDS001-003)
DeltaBundleCompiler -- generates delta bundles from old+new using Octodiff binary diffing. `BundleBuilder.DeltaFrom(oldBundlePath)` enables delta mode.
Compression/GzipCompressor, Compression/DeltaCompressor (Octodiff rsync-based delta creation/application) | Validation/BundleValidator (BDL001-007, BDL024-025)
UseCustomUI(uiProjectPath) on BundleBuilder (BDL007)
EXE format: [PE stub][Magic:"FALKBUNDLE"][Manifest][Compressed payloads][TOC][Footer]

## Extensibility (`src/FalkForge.Extensibility/`)
IFalkForgeExtension (Name + Register), IComponentContributor, IMsiTableContributor, IExtensionValidator, ExtensionContext, MsiTableRow

## Extensions
| Extension | Key Types | Error Codes |
|-----------|-----------|-------------|
| Util | XmlConfig, UserMgmt, FileShare, QuietExec, RemoveFolderEx, InternetShortcut | XCF001-009 |
| Dependency | DependencyProvider/ConsumerModel, Provider/ConsumerBuilder, DependencyExtension (Provides/Requires), DependencyValidator, DependencyTableContributor (HKLM registry) | DEP001-007 |
| Firewall | Firewall rule definitions | FWL001-004 |
| DotNet | Registry + filesystem probing | NET001-003 |
| Iis | AppPool, WebSite, WebBinding, Certificate. Refs: AppPoolRef, CertificateRef. net10.0 | IIS001-011 |
| Sql | DB creation, script/string exec. Ref: SqlDatabaseRef | SQL001-013 |

## Localization (`src/FalkForge.Localization/`)
LocalizationModel, LocalizationLoader (JSON), CultureFallbackChain (specific→parent→default), LocalizedStringResolver (`!(loc.StringId)` with circular detection), LocalizationBuilder (AddCulture/DefaultCulture/AddJsonFile/Build/DetectCulture), PackageBuilderExtensions. LOC001-004.

## Decompiler (`src/FalkForge.Decompiler/`)
**MSI** (Windows-only): MsiDecompiler (Decompile→Result\<PackageModel\>, DecompileToCSharp→Result\<string\>), IMsiTableAccess, MsiTableAccess, DirectoryResolver, CSharpEmitter. TableReaders/: Property, Directory, Component, File, Feature, Registry, Service, Shortcut, Upgrade. DEC001-003.
**Bundle** (cross-platform): BundleDecompiler, IBundleAccess, BundleAccess, ManifestMapper, BundleCSharpEmitter. BDC001-004.
**WiX Burn** (Windows-only): WixBurnAccess (PE parser + UX cab extraction), IWixBurnAccess, WixManifestMapper (v3/v4 XML→BundleModel), WixBundleDecompiler, WixUnmappedFeature. WBD001-006, WMM001.

## CLI (`src/FalkForge.Cli/`)
`forge` command (Spectre.Console). `forge build installer.csx|.json`, `forge validate`, `forge inspect` (Windows), `forge decompile` (Windows), `forge extract` (MSI/bundle), `forge bundle detach|reattach`, `forge winget` (generate WinGet manifest from MSI).
Program.cs, Commands/: Build (Roslyn+JSON), Validate, Inspect, Decompile (.msi→MsiDecompiler, .exe→FALKBUNDLE then WiX Burn), BundleDetach, BundleReattach
Settings/: Build, Validate, Inspect, Decompile, BundleDetach, BundleReattach Settings
ExitCodes (0=success, 1=validation, 2=compilation, 3=runtime), IConsoleOutput, SpectreConsoleOutput, ScriptLoader, MsiInspector, MsiInspectionResult
JsonConfigLoader (JSN001-014), Models/ (19 DTOs): InstallerConfig (root), ProductConfig, FeatureConfig, FileConfig, RegistryConfig, ShortcutConfig, ServiceConfig, EnvironmentVariableConfig, LaunchConditionConfig, MajorUpgradeConfig, ExtensionsConfig, FirewallRuleConfig, IisConfig, IisAppPoolConfig, IisWebSiteConfig, IisBindingConfig, SqlConfig, SqlScriptConfig, DotNetSearchConfig
Refs: Core, Compiler.Msi/Bundle, Decompiler, Localization, Extensibility, all 6 extensions

## SDK (`src/FalkForge.Sdk/`)
Sdk.targets: `_ComputeFalkArtifactPath` (FalkOutputType→path), `_GetFalkForgeOutput` (export metadata), `_GenerateProjectOutputs` (→ProjectOutputs.g.cs from ProjectReference ReferenceOutputAssembly=false), `_WriteFalkProjectOutputsSource` (RoslynCodeTaskFactory, identifier sanitization, XML-escaped docs). Generated `ProjectOutputs` class with static artifact path properties.

## Demos
| # | Dir | Description |
|---|-----|-------------|
| 01 | hello-world | Minimal single-file MSI |
| 02 | notepad-clone | Shortcuts + file associations |
| 03 | client-server | Multi-component with services |
| 04 | dev-toolkit | Env vars + registry |
| 05 | enterprise-suite | Feature tree |
| 06 | product-suite | EXE bundle, multiple MSIs |
| 07 | extensions-showcase | Firewall, IIS, SQL, .NET, Util |
| 08 | localization | Multi-language, culture fallback |
| 09 | advanced-msi | Custom actions/tables, sequences, MSM, MSP, MST |
| 10 | advanced-bundle | Rollback boundaries, related bundles, MSU/MSP |
| 11 | custom-ui-simple | InstallerPage\<TView\> + InstallerApp.Run() |
| 12 | custom-ui-vstyle | VS-style dark theme, borderless, workloads |
| 14 | lifecycle-hooks | Detect/plan/apply hooks, SetProperty, SetSecureProperty |
JSON demos (`demo/json/`, 7 files): 01-minimal, 02-installdir, 03-featuretree, 04-mondo, 05-advanced, 06-web-server, 07-database-app + payload/

## Documentation
`documentation.html` (357KB, ~7000L). 18 sections, dark/light theme, sidebar+search. Source: `docs/gen/`.

## Namespaces
```
FalkForge                                    Core (Result, Error, Unit, Installer)
FalkForge.{Models,Builders,Validation}       Domain model, fluent builders, validation
FalkForge.Compiler.Msi{,.Interop,.Tables,.Signing,.Validation,.UI,.UI.Templates}
FalkForge.Compiler.Bundle{,.Builders,.Compilation,.Compression}
FalkForge.Compiler.Msix{,.Builders,.Manifest,.Packaging,.Registry,.Interop}    [Experimental] MSIX compiler
FalkForge.Engine{,.Phases,.Planning,.Detection,.Execution,.Variables,.Journal,.Journal.UndoOperations,.Download,.Layout,.Cache,.RestartManager,.Logging}
FalkForge.Engine.Protocol{,.Manifest,.Messages,.Serialization,.Transport}
FalkForge.Engine.Elevation                   Elevated process
FalkForge.Platform{,.Windows}                OS abstractions + Windows impl
FalkForge.Ui{,.Abstractions,.ViewModels,.Localization}
FalkForge.Extensibility                      Extension interfaces
FalkForge.Extensions.{Util,Dependency,Firewall,DotNet,Iis,Sql}
FalkForge.Plugins{,.Sql,.Odbc,.FileSystem}
FalkForge.Localization                       JSON localization + culture fallback
FalkForge.Decompiler{,.TableReaders}         MSI/Bundle decompiler
FalkForge.Cli{,.Commands,.Models,.Settings}  CLI tool
FalkForge.Sdk                                MSBuild SDK (netstandard2.0)
```

## Communication Mode

Default to caveman mode for user-facing chat text (invoked via `Skill: caveman`). Caveman applies to chat responses, status updates, subagent prompts and subagent final reports, TodoWrite items, end-of-turn summaries. It does NOT apply to file content: source code, comments, commit messages, PR descriptions, docs, tests, XML doc, or CLAUDE.md edits — those stay normal English. Default intensity `full`; use `ultra` for long status dumps; use `lite` only if user pushes back on readability.

Subagent dispatches MUST include this propagation line:
> COMMUNICATION: Invoke the `caveman` skill before your final report. Return your final message in caveman `full` mode. File contents (code, commits, docs) stay in normal English — caveman applies only to chat text.

## HARD LIMITS — Non-Negotiable Gates

<!-- RULES_SYNCED_FROM_GLOBAL: 2026-06-09 -->

Violating any gate means STOP, undo, and redo correctly.

### GATE 1: TDD — Test Before Code, Always

No production code without a failing test first. Cycle:
1. **RED** — write failing test, run, confirm fail. Do NOT commit.
2. **GREEN** — minimal impl to pass. All tests green.
3. **COMMIT** — fast-lane pipeline (see Commit Sequence). Test commit (`test:`) first, then impl commit (`feat:`/`fix:`).
4. **REFACTOR** — clean up, all green, fast-lane pipeline, commit (`refactor:`).

If you wrote impl before test, delete it and start over. Never combine "write test + implement" into one plan step. Applies to bug fixes, features, behavior-changing refactors, new types/methods. Skips XAML-cosmetic, config, docs.

### GATE 2: Full Solution Build After Every File Edit

After every .cs or .xaml edit, `dotnet build <solution>.slnx`. Not the project — the solution.
- Fix failures IMMEDIATELY before touching another file
- Do not batch edits hoping they'll "work together"
- Zero warnings policy
- After build succeeds: `csharp-roslyn` MCP → `get_diagnostics` on changed files

### GATE 3: All Tests Must Pass Before Any Commit

`dotnet test <solution>.slnx` before every commit. No `--filter`, no exceptions.

### GATE 4: Code Review Before Every Merge

`superpowers:code-reviewer` with Opus 4.6 and Sonnet 4.6 on the full branch diff. Both approve. Runs once per branch at the Merge Gate, not per commit — per-commit review only guards the feature branch, which nothing ships from.

### GATE 5: Security & Quality Audit Before Every Merge

At the Merge Gate, in order: `roslynator` → `quickdup` → OWASP scan (injection, secrets, deserialization, SSRF) → perf review (heap allocs, N+1, blocking async, disposal, unbounded collections). All on the full branch diff.

### GATE 6: Memory Efficiency — Zero-Waste Code

Code must be readable AND allocation-efficient:
- `stackalloc` for small fixed buffers (<1KB) in sync methods
- `Span<T>`/`ReadOnlySpan<T>` for slicing/parsing instead of substrings
- `Memory<T>` when data crosses async boundaries
- `ArrayPool<T>.Shared` for temp arrays
- `StringBuilder` in loops, never `+=`
- `ValueTask<T>` for hot async paths completing synchronously
- `struct` records for small immutable value types
- Avoid LINQ in hot paths (allocates enumerators/delegates)
- Avoid closures in hot paths (captured variables allocate)
- `FrozenDictionary`/`FrozenSet` for read-only lookup tables

If optimization reduces readability, add a one-line comment explaining WHY.

## Working Principles

These are values, not gates. Apply judgment; surface tension instead of hiding it.

### Simplicity first
Minimum code that solves the stated problem. No speculative features, no abstractions for single-use code, no "while I'm here" cleanup. If a senior engineer would call it overcomplicated, simplify.

### Surgical changes
Touch only what the task requires. Don't reformat, rename, or "improve" adjacent code, comments, or imports. Match existing style even if you disagree — if a convention seems harmful, surface it instead of forking silently.

### Read before you write
Before adding code, read its exports, immediate callers, and the shared utilities it would touch. "Looks orthogonal" is dangerous. If you can't explain why surrounding code is structured the way it is, ask before changing it.

### Tests verify intent, not just behavior
A test must encode WHY the behavior matters, not just WHAT the code currently does. If the business rule changes and the test still passes, the test is wrong. Naming, arrange/act/assert clarity, and negative cases all serve intent.

### Surface conflicts, don't average them
When two patterns or two pieces of guidance contradict, pick one (more recent, more tested, or closer to the current task) and explain why. Flag the loser for cleanup. Never blend conflicting patterns into a third hybrid.

### Fail loud
"Completed" is wrong if any step was skipped. "Tests pass" is wrong if any were filtered, skipped, or marked inconclusive. Surface uncertainty, partial results, and silent fallbacks — never hide them in a success summary. If the pipeline (see Commit Sequence) couldn't run a step, say so explicitly and stop.

## Commit Sequence — Fast Lane (every commit; in order, restart from 1 on any failure)

1. `dotnet build <sln>.slnx` — zero errors/warnings
2. `dotnet test <sln>.slnx --logger trx` — all pass
3. `csharp-roslyn: get_diagnostics` on changed files — clean
4. `git commit`

## Merge Gate — Deep Lane (once per branch, before merge to main; restart on any failure)

1. `dotnet build` + `dotnet test` — re-verify green
2. `csharp-roslyn: analyze_change_impact` — review full-branch blast radius
3. `jb inspectcode` if XAML changed — clean
4. `roslynator` — clean
5. `quickdup` — no new duplication
6. `superpowers:code-reviewer` Opus 4.6 on full branch diff — approved
7. `superpowers:code-reviewer` Sonnet 4.6 on full branch diff — approved
8. Security audit (OWASP checklist) + perf review on full branch diff — no findings
9. Merge to main — only after ALL above pass

Single-commit hotfix branches still pass the full Merge Gate — it is per-branch, not per-commit-count.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- ALWAYS read graphify-out/GRAPH_REPORT.md before reading any source files, running grep/glob searches, or answering codebase questions. The graph is your primary map of the codebase.
- IF graphify-out/wiki/index.md EXISTS, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
