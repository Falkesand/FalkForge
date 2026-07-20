# FalkForge Codebase Map

Detailed project inventory, dependency graph, key patterns, per-project layout, and namespaces.
Loaded on demand (referenced from `CLAUDE.md`, not eagerly imported). For architecture prose and
API reference, `documentation.html` is the source of truth; for cross-module relationships, prefer
the `graphify-out/` knowledge graph.

> This is a reference map and can drift from the code. When a claim here conflicts with the source,
> the source wins — verify before relying on a specific type/file name.

## Solution (37 src + 30 test projects)
| Project | Purpose |
|---------|---------|
| Core | Domain model, fluent API, validation |
| Meta (`src/FalkForge.Meta`, PackageId `FalkForge`) | Batteries-included meta-package: no library, only deps — Core, both compilers, Localization, Extensibility, all 8 extensions, Engine.Runtime.win-x64. One `dotnet add package FalkForge` = working setup with runnable bundles. |
| Templates (`FalkForge.Templates`, PackageType Template) | `dotnet new falkforge-msi` / `falkforge-bundle` project templates under `content/`; reference the meta-package; version default pinned to single-source by TemplatePackTests. |
| Compiler.Msi | MSI/MSM/MSP/MST generation via msi.dll P/Invoke |
| Compiler.Bundle | Self-extracting EXE bundle compiler |
| Compiler.Msix | **[Experimental]** MSIX/.msixbundle compiler. Studio wired; CLI dispatch not implemented. Entry point: `InstallerMsix.BuildMsix()` / `BuildMsixBundle()`. |
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
| Extensions.Driver | Device driver install via PnP: DriverModel, DriverBuilder, DriverTableContributor, DriverValidator |
| Extensions.Http | URL ACL + SNI SSL bindings via netsh: Builders, Models, Validation, Compilation |
| Studio | WPF visual installer builder (work in progress) |
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
| Signing.SignServer | Remote signing via Keyfactor SignServer |

Tests mirror src: `FalkForge.{Project}.Tests/` (30 projects incl. Integration.Tests, Platform.Windows.Tests)

## Dependency Graph
```
Core (no deps)
  ├→ Platform → Platform.Windows
  ├→ Engine.Protocol (AOT-safe) → Ui.Abstractions → Ui (WPF+ReactiveUI)
  │                              └→ Compiler.Bundle
  ├→ Compiler.Msi (Core + Platform)
  ├→ Compiler.Msix (Core + Platform; Windows-only; experimental)
  ├→ Extensibility (standalone)
  ├→ Extensions.* (Core + Extensibility; DotNet also + Platform; Driver/Http standalone)
  ├→ Testing (Core + Platform), Localization (Core)
  ├→ Decompiler (Core + Compiler.Msi + Compiler.Bundle)
  └→ Plugins.Sql (Core + SqlClient), Plugins.Odbc/FileSystem (Core, Windows-only)
Engine exe:    Engine.Protocol + Platform.Windows + Compiler.Msi
Elevation exe: Engine.Protocol + Platform.Windows
Cli exe:       Core + Compiler.Msi/Bundle + Decompiler + Localization + Extensibility + Extensions.*
Meta pkg (PackageId FalkForge): deps only — Core + Compiler.Msi/Bundle + Localization + Extensibility + Extensions.* + Engine.Runtime.win-x64
Templates pkg: content-only template pack (dotnet new falkforge-msi|bundle) → scaffolded project references Meta pkg
```

## Key Patterns

**Result\<T\>** (`Core/Result.cs`) -- readonly record struct. `.Success(value)` / `.Failure(error)`. Match/Map/Bind.
**Error** (`Core/Error.cs`) -- `record struct Error(ErrorKind Kind, string Message)`. ErrorKind: 29 values (Validation, FileNotFound, CompilationError, SecurityError, ProtocolError, EngineError, ElevationError, BundleError, DownloadError, LayoutError, etc.)
**Unit** (`Core/Unit.cs`) -- `readonly record struct` for `Result<Unit>`.
**Entry Points** (`Core/Installer.cs`) -- `Installer.Build()` MSI, `.BuildBundle()` EXE, `.BuildMergeModule()` MSM, `.BuildPatch()` MSP, `.BuildTransform()` MST.

**WinGet** (`Core/WinGet/`) -- `PackageBuilder.WinGet(w => w.PackageIdentifier("Publisher.App").License("MIT").ShortDescription("..."))`. Auto-generates 3-file WinGet manifest (version + installer + locale YAML) alongside MSI output. SHA-256 computed at compile time. `forge winget <msi>` CLI for existing MSIs.
**Delta Updates** (`Compiler.Bundle/Compilation/DeltaBundleCompiler`) -- `BundleBuilder.DeltaFrom(oldBundlePath)` generates delta bundles using Octodiff. Only changed payload bytes are included. Engine downloads delta-first, falls back to full bundle. TOC uses flag byte for backward compatibility.
**PackageCode** -- `PackageModel.PackageCode` (`Guid?`): `PackageBuilder.Build()` assigns fresh GUID per build (normal) or `null` (reproducible); compiler derives content-digest UUID v5 via `PackageCodeDerivation` so non-identical packages never share a PackageCode (SECREPAIR, issue #1).

**MsiProperty** (`Core/MsiProperty.cs`) -- Type-safe MSI property refs. ~46 built-in statics. `Custom(string)` factory. `/` for path composition. Comparison operators return `Condition`.
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

**EnvVarCatalog** (`Core/Configuration/EnvVarCatalog.cs`) -- Central typed catalog of all FALKFORGE_*/SIGNSERVER_* env vars: name constants + validated accessors (TryGetSourceDateEpoch, IsSigningDisabled, IsSbomGenerationRequested) + GetRaw/SetRaw. Validation is at point-of-use (fail loud), not a blanket startup gate.

## Core Layout (`src/FalkForge.Core/`)
**Models** (50+ files): PackageModel, FeatureModel, ComponentModel, FileEntryModel, DirectoryModel, BinaryModel | MergeModuleModel, PatchModel, TransformModel | ServiceModel, ServiceControlModel, ServiceDependencyModel | RegistryEntryModel, RemoveRegistryModel, RemoveRegistryAction | MoveFileModel, DuplicateFileModel, RemoveFileModel, CreateFolderModel | CustomActionModel, CustomActionType | CustomTableModel, CustomTableColumnModel, CustomTableColumnType | SequenceTable, SequenceActionModel, SequencePosition | MajorUpgradeModel, DowngradeModel, RemoveExistingProductsSchedule, UpgradeModel | ShortcutModel, EnvironmentVariableModel, AssemblyModel, AssemblyType, MediaTemplateModel, FeatureConditionModel, SigningOptions, ExitCodeBehavior, RelatedBundleRelation, LaunchConditionModel, PropertyModel, LocalizationData, VerbModel, FontModel, IniFileModel, FileAssociationModel, PermissionModel

**Builders** (34+ files): PackageBuilder (main) | MergeModuleBuilder, PatchBuilder, TransformBuilder | FeatureBuilder | FileSetBuilder, MoveFileBuilder, DuplicateFileBuilder, RemoveFileBuilder, CreateFolderBuilder | ServiceBuilder, ServiceControlBuilder, ServiceFailureActionsBuilder | RegistryBuilder, RegistryKeyBuilder, RemoveRegistryBuilder | CustomActionBuilder (incl. simplified DllFromBinary overload) | CustomTableBuilder, ColumnOptions, RowBuilder | SequenceBuilder | ShortcutBuilder, EnvironmentVariableBuilder, AssemblyBuilder, MajorUpgradeBuilder, DowngradeBuilder, MediaTemplateBuilder, UpgradeBuilder, SigningOptionsBuilder, PropertyBuilder, PermissionBuilder, VerbBuilder, FileAssociationBuilder, FontBuilder, IniFileBuilder

**WinGet/** (`Core/WinGet/`): WinGetManifestWriter (3-file YAML manifest generation), WinGetConfig model. PackageBuilder.WinGet() fluent configuration.

**Validation** (`Core/Validation/`): PKG001-011, FEA001-005, SVC001-005/SVC009-012, REG001-003/REG007/RRG001-003, CTB001-011, MUP001/003, DNG001-002 | MergeModuleValidator MSM001-004 | PatchValidator MSP001-004 | TransformValidator MST001-002. (REG001-003 = empty-key error + scope-aware duplicate detection; SVC012 = service account without password.)

## Compiler.Msi (`src/FalkForge.Compiler.Msi/`)
Compilers: MsiCompiler (ICompiler), MsmCompiler, PatchCompiler, TransformCompiler
DB: MsiDatabase, MsiRecord, FileNameSanitizer, ResolvedPackage/Component/File, ComponentResolver, SummaryInfoWriter
Cabinets: CabinetBuilder (single-threaded), ParallelCabinetBuilder (Parallel.ForEachAsync), CabinetWorkItem, CabinetBuildResult, CabinetExtractor (FDI)
Tables/: MsiTableDefinitions, EnvironmentEncoding
Recipe/: MsiAuthoring, MsiRecipeBuilder, MsiDatabaseRecipe, ITableProducer, IMultiTableProducer, RecipeBuildContext, RecipeTable, RecipeRow, RecipeColumn, CellValue, TableId, TableSchema, ForeignKeySpec, DirectoryTreeSynthesizer
Recipe/Producers/: ComponentTableProducer, DirectoryTableProducer, DialogSetProducer, FeatureTableProducer, FeatureComponentsTableProducer, PropertyTableProducer, RegistryTableProducer, ShortcutTableProducer, ServiceInstallTableProducer, UpgradeTableProducer, and others. ValidateCustomTableIdentifiers() defense-in-depth SQL identifier validation enforced by CustomTablesProducer.
Interop/: NativeMethods.Msi (msi.dll LibraryImport), NativeMethods.Cabinet (cabinet.dll), MsiDatabaseHandle, MsiRecordHandle, MsiViewHandle, FciHandle, FdiHandle. Assembly-level DefaultDllImportSearchPaths(System32) prevents DLL hijacking.
Signing/, Validation/IceValidator (validates a disposable temp copy — never mutates the shipped MSI)
UI/: MsiDialogModel, MsiControlModel, MsiControlEventModel, MsiControlConditionModel, IDialogTemplate. Dialog tables produced via DialogSetProducer in the recipe pipeline. Custom dialog authoring via PackageBuilder.AddCustomDialog + CustomDialogTranslator (DLG010-022).
UI/Templates/: Minimal, InstallDir, FeatureTree, Mondo, Advanced DialogTemplates
BuiltInLocalizationExtensions (`AddBuiltInCultures()`), Localization/en-US.json + sv-SE.json (48 keys each)

## Engine Architecture (3-process model)
```
[UI WPF+ReactiveUI] <--Named Pipe A--> [Engine NativeAOT] <--Named Pipe B--> [Elevated NativeAOT]
```
Phases: Initializing → Detecting → Planning → Elevating → Applying → Completing → Shutdown. Error: any → Failed → RollingBack → Shutdown.

**Engine** (`src/FalkForge.Engine/`):
- EngineSession + EngineSessionOptions (session lifecycle: manifest path, plan-only mode, log config). Pipeline/: IInstallerPipeline/InstallerPipeline (Detect/Plan/Elevate/Apply/ExportPlan/LaunchUpdate), PipelineRunner (drives the UiRequest loop: Detect → Plan → [plan-only: export + exit] → Elevate → Apply → Shutdown), PipelineContext (holds Plan + session state), IPhaseStep + step classes (DetectStep, PlanStep, ElevateStep, ApplyStep, RollbackStep). IUiChannel: NamedPipeUiChannel dispatches SetProperty/SetSecureProperty to pending property dictionaries via PropertyNameValidator (regex check, built-in variable blocking, max length enforcement) before bundling into UiRequest.Plan; NullUiChannel for headless use.
- Detection/: PackageDetector, MsiDetector, DependencyDetector, DependencyBlocker
- Planning/: Planner, InstallPlan, PlanAction
- Execution/: PackageExecutor, MsiExecutor (uses IMsiApi P/Invoke InstallProduct/ConfigureProduct instead of msiexec.exe; 3-arg ctor with Func<IMsiApi?> lazy accessor; property value injection defense via ProhibitedValueChars), MsuExecutor, MspExecutor, BundleExecutor, ExitCodeMapping, ExecutionOutcome, IProcessRunner, ProcessRunner
- Variables/: VariableStore (30+ built-ins), BuiltInVariables, SecureVariable (IDisposable, zeroed on dispose), ConditionEvaluator (recursive-descent), ConditionLexer, ConditionToken, TokenType
- Download/: PayloadDownloader (HTTP+retry+SHA256), UpdateFeed, UpdateFeedEntry, UpdateInfo, UpdateCheckResult, UpdateFeedJsonContext (AOT), UpdateFeedParser (UPD002-003), UpdateDownloader (delta-first with full-bundle fallback)
- Layout/: LayoutManager, LayoutJsonContext | Cache/: PackageCache, CacheLayout (three-layer path traversal defense: allowlist regex, Path.GetFileName sanitization, Path.GetFullPath containment check)
- Journal/: RollbackJournal, JournalEntry, RollbackExecutor | UndoOperations/: IUndoOperation, MsiUninstallOperation, ExeRollbackOperation, CacheCleanupOperation
- RestartManager/: IRestartManager, RestartManagerSession, RestartManagerProcess, NativeRestartManagerMethods
- Logging/: EngineLogger (per-session GUID subdirectory for unpredictable log paths). The logging contract — `IFalkLogger`, `LogLevel`, `LogEntry`, `NullLogger`, `LogProperties` — lives in `FalkForge.Core` namespace `FalkForge.Diagnostics` (shared by Engine, Compiler.Msi, Decompiler, Plugins); EngineLogger implements it. `EngineMeter` bridges metrics via `FlushToLogger(IFalkLogger)`.
- Bootstrap/: PreUIBootstrapOrchestrator, PreUIPrerequisiteDetector/Installer (+ interfaces IPreUIPrerequisiteDetector/Installer), PreUIBootstrapOutcome, PreUIResult, IProgressSink, IProgressSinkFactory, DefaultBootstrapAdapters, WindowsFileSystemProvider, ElevatedSelfRelauncher/IElevatedSelfRelauncher, ElevationProbe/IElevationProbe, Native/ — native pre-UI prerequisite bootstrap (RunAsBootstrapper)

**Engine.Protocol** (`src/FalkForge.Engine.Protocol/`):
- Messages/ (29+ types): Detect/Plan/Apply Begin/Complete, Request Detect/Plan/Apply, Progress, Error, PhaseChanged, Cancel, Log, Shutdown, ElevateExecute/Progress/Result, UpdateAvailable/DownloadProgress/Ready, SetPropertyMessage (0x0208), SetSecurePropertyMessage (0x0209), SessionStart, License, LaunchUpdate, per-package + per-related-bundle lifecycle messages, SetFeatureSelection, SetInstallDirectory, ShutdownRequest/Response
- Serialization/: MessageSerializer, MessageDeserializer -- binary [Version:u16][Type:u16][Length:i32][Payload]. MessageCodecRegistry.
- Transport/: PipeServer, PipeClient, PipeConnectionOptions, PipeSecurityValidator -- HMAC-SHA256 handshake
- Manifest/: InstallerManifest (sealed record; + IsDeltaUpdate, BaseVersion, BaseBundleSha256, EngineCompanionSha256), PackageInfo, PackageType, RelatedBundleEntry, RollbackBoundaryInfo, ManifestChainItem, PackageManifestChainItem, RollbackBoundaryManifestChainItem, ManifestDependencyProvider/Consumer, UpdatePolicy (NotifyOnly/DownloadAndPrompt/AutoUpdate), ManifestUpdateFeed. TocEntry gains IsDelta, BaseSha256Hash, ReconstructedSha256Hash for delta payloads. UpdateFeedEntry gains DeltaUrl, DeltaSha256, DeltaSize.
- Integrity/: EcdsaManifestSigner, PayloadHashEntry, ManifestSignatureEnvelope, IntegrityEnvelopeCodec (shared by MSI + bundle signing; pure-.NET ECDSA-P256, low-S normalized, optional hybrid ML-DSA-65).

**Engine.Elevation** (`src/FalkForge.Engine.Elevation/`):
ElevatedHost (args, PID verify + PID recycling defense via parent start time capture, HMAC), ElevatedCommandExecutor (whitelisted dispatch), ElevationSecurityLog (file-based security event logger, thread-safe, NativeAOT-compatible), Commands/: MsiInstall, MsiUninstall (both use IMsiApi P/Invoke, not msiexec.exe), ServiceInstall, RegistryWrite, FileWrite

**IInstallerEngine Property Passing**: `SetProperty(name, value)` sends SetPropertyMessage via pipe → stored in VariableStore + UserProperties → forwarded to MsiExecutor as `PROPERTY=value`. `SetSecureProperty(name, SensitiveBytes)` sends SetSecurePropertyMessage via pipe → stored as SecureVariable (zeroed on dispose), tracked in SecretPropertyNames → passed to MSI via IMsiApi.SetProperty (never CLI). Use PasswordBridge + GetPassword + SetSecureProperty.

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
Compilation/: BundleCompiler, ManifestGenerator, ManifestJsonContext, PayloadEmbedder, PayloadEntry, BundleContent, TocEntry, ElevationCompanionLocator/Appender
**Elevation companion** -- a runnable bundle carries `FalkForge.Engine.Elevation.exe` as a trust-covered payload by default (reserved TOC id `EngineCompanionPayload.PackageId`, SHA-256 declared in `InstallerManifest.EngineCompanionSha256`, inside the ECDSA envelope when `Integrity()` is on). Resolution: explicit `ElevationCompanionPath` → `FALKFORGE_ELEVATION_COMPANION` env → beside the engine stub; opt-out `BundleBuilder.WithoutElevationCompanion()`. At bootstrap the engine verifies the extracted companion (`BootstrapCompanionResolver`: bytes==TOC==declared, fail closed) and wires it via `EngineSessionOptions.ElevationCompanionPath`, enabling per-machine elevated installs from a lone distributed exe. The bootstrap threads an explicit `ElevationCompanionPolicy` (VerifiedPath | NoneDeclared) so the manifest is authoritative — a bundle declaring no companion never falls back to the ambient beside-the-engine probe (planted-binary defense); `AmbientAllowed` (default) keeps the probe for plain engine runs. On signed bundles `SignedPayloadTocVerifier` binds `EngineCompanionSha256` into the signed set both directions (INT012): an edited/stripped declaration is tamper (abort), not a silent per-user downgrade.
BundleDetacher -- Detach/Reattach for code signing (BDS001-003)
DeltaBundleCompiler -- generates delta bundles from old+new using Octodiff binary diffing. `BundleBuilder.DeltaFrom(oldBundlePath)` enables delta mode.
Compression/GzipCompressor, Compression/DeltaCompressor (Octodiff rsync-based delta creation/application) | Validation/BundleValidator (BDL001-007, BDL024-025)
UseCustomUI(uiProjectPath) on BundleBuilder (BDL007)
EXE format: [PE stub][Magic:"FALKBUNDLE"][Manifest][Compressed payloads][TOC][Footer]
Known gap: `Reproducible()` + `Integrity()` on bundles is not byte-identical yet (signature embedded in-band; MSI moves it to a sidecar). Post-beta task.

## Extensibility (`src/FalkForge.Extensibility/`)
IFalkForgeExtension (Name + Register), IComponentContributor, IMsiTableContributor, IExtensionValidator, ExtensionContext, MsiTableRow. Extensions attach via `new MsiCompiler().Use(extension)` — no auto-discovery (NativeAOT, explicit by design).

## Extensions
| Extension | Key Types | Error Codes |
|-----------|-----------|-------------|
| Util | XmlConfig, UserMgmt, FileShare, QuietExec, RemoveFolderEx, InternetShortcut | XCF001-009 |
| Dependency | DependencyProvider/ConsumerModel, Provider/ConsumerBuilder, DependencyExtension (Provides/Requires), DependencyValidator, DependencyTableContributor (HKLM registry) | DEP001-007 |
| Firewall | Firewall rule definitions | FWL001-004 |
| DotNet | Registry + filesystem probing | NET001-003 |
| Iis | AppPool, WebSite, WebBinding, Certificate, virtual directories. Refs: AppPoolRef, CertificateRef. net10.0 | IIS001-017 |
| Sql | DB creation, script/string exec. Ref: SqlDatabaseRef | SQL001-013 |
| Driver | Device driver install via PnP (INF-based). DriverModel, DriverBuilder, DriverTableContributor, DriverValidator | DRV001+ |
| Http | URL ACL reservation + SNI SSL certificate bindings via netsh. HttpBuilder, Models, Validation, Compilation | HTTP001+ |

## Localization (`src/FalkForge.Localization/`)
LocalizationModel, LocalizationLoader (JSON), CultureFallbackChain (specific→parent→default), LocalizedStringResolver (`!(loc.StringId)` with circular detection), LocalizationBuilder (AddCulture/AddBaselineCulture/DefaultCulture/AddJsonFile/Build/DetectCulture — baseline tier lets user cultures override built-in strings; same-tier duplicate = LOC001), PackageBuilderExtensions. LOC001-004. `forge loc export` dumps the built-in en-US/sv-SE JSON.

## Decompiler (`src/FalkForge.Decompiler/`)
**MSI** (Windows-only): MsiDecompiler (Decompile→Result\<PackageModel\>, DecompileToCSharp→Result\<string\>), IMsiTableAccess, MsiTableAccess, DirectoryResolver, CSharpEmitter. TableReaders/: Property, Directory, Component, File, Feature, Registry, Service, Shortcut, Upgrade. DEC001-003.
**Bundle** (cross-platform): BundleDecompiler, IBundleAccess, BundleAccess, ManifestMapper, BundleCSharpEmitter. BDC001-004.
**WiX Burn** (Windows-only): WixBurnAccess (PE parser + UX cab extraction), IWixBurnAccess, WixManifestMapper (v3/v4 XML→BundleModel), WixBundleDecompiler, WixUnmappedFeature. WBD001-006, WMM001.

## CLI (`src/FalkForge.Cli/`)
`forge` command (Spectre.Console). `forge init` (scaffold starter project referencing the `FalkForge` meta-package; `--type msi|bundle`, `--name`, `--from-publish`, `--force`; content generation in `InitScaffolder`), `forge build installer.csx|.json`, `forge validate`, `forge inspect` (Windows), `forge decompile` (Windows), `forge extract` (MSI/bundle), `forge bundle detach|reattach`, `forge winget`, `forge verify` (MSI ECDSA signature: bidirectional content binding, --trusted-key, table-first/sidecar-fallback), `forge plan` / `forge plan-diff`, `forge migrate`, `forge loc export`, `forge rules list|explain`.
Program.cs, Commands/: Build (Roslyn+JSON), Validate, Inspect, Decompile, BundleDetach, BundleReattach, Verify, Plan, PlanDiff, Migrate, LocExport, etc.
Settings/: per-command Settings classes.
ExitCodes (0=success, 1=validation, 2=compilation, 3=runtime), `FromErrorKind(ErrorKind)` mapping, IConsoleOutput, SpectreConsoleOutput, ScriptLoader, MsiInspector, MsiInspectionResult
JsonConfigLoader (JSN001-019), Models/ (19 DTOs): InstallerConfig (root), ProductConfig, FeatureConfig, FileConfig, RegistryConfig, ShortcutConfig, ServiceConfig, EnvironmentVariableConfig, LaunchConditionConfig, MajorUpgradeConfig, ExtensionsConfig, FirewallRuleConfig, IisConfig, IisAppPoolConfig, IisWebSiteConfig, IisBindingConfig, SqlConfig, SqlScriptConfig, DotNetSearchConfig
Refs: Core, Compiler.Msi/Bundle, Decompiler, Localization, Extensibility, all 8 extensions

## SDK (`src/FalkForge.Sdk/`)
Sdk.targets: `_ComputeFalkArtifactPath` (FalkOutputType→path), `_GetFalkForgeOutput` (export metadata), `_GenerateProjectOutputs` (→ProjectOutputs.g.cs from ProjectReference ReferenceOutputAssembly=false), `_WriteFalkProjectOutputsSource` (RoslynCodeTaskFactory, identifier sanitization, XML-escaped docs). Generated `ProjectOutputs` class with static artifact path properties.

## Demos
`demo/` (65 numbered demos + `demo/json/` 7 JSON configs). Full annotated list is in `documentation.html` §19 (Demos) — the manual is the maintained source. Highlights: 01 hello-world, 06 product-suite (bundle), 09 advanced-msi, 11/12/13 custom UI, 47 powershell-actions, 51 ice-validation, 53 delta-updates, 54 forge-migrate, 56 verify-and-plan, 57 reproducible-sbom, 59-63 signing (integrity/rotation/SignServer/require-signed/hybrid-PQ), 64 AcmeSuite capstone, 65 custom-dialog.

## Namespaces
```
FalkForge                                    Core (Result, Error, Unit, Installer)
FalkForge.{Models,Builders,Validation}       Domain model, fluent builders, validation
FalkForge.Configuration                      EnvVarCatalog
FalkForge.Compiler.Msi{,.Interop,.Tables,.Signing,.Validation,.UI,.UI.Templates}
FalkForge.Compiler.Bundle{,.Builders,.Compilation,.Compression}
FalkForge.Compiler.Msix{,.Builders,.Manifest,.Packaging,.Registry,.Interop}    [Experimental] MSIX compiler
FalkForge.Engine{,.Pipeline,.Planning,.Detection,.Execution,.Variables,.Journal,.Download,.Layout,.Cache,.RestartManager,.Logging,.Bootstrap}
FalkForge.Engine.Protocol{,.Manifest,.Messages,.Serialization,.Transport,.Integrity}
FalkForge.Engine.Elevation                   Elevated process
FalkForge.Platform{,.Windows}                OS abstractions + Windows impl
FalkForge.Ui{,.Abstractions,.ViewModels,.Localization}
FalkForge.Extensibility                      Extension interfaces
FalkForge.Extensions.{Util,Dependency,Firewall,DotNet,Iis,Sql,Driver,Http}
FalkForge.Plugins{,.Sql,.Odbc,.FileSystem}
FalkForge.Localization                       JSON localization + culture fallback
FalkForge.Decompiler{,.TableReaders}         MSI/Bundle decompiler
FalkForge.Cli{,.Commands,.Models,.Settings}  CLI tool
FalkForge.Sdk                                MSBuild SDK (netstandard2.0)
FalkForge.Studio                             WPF visual installer builder
```
