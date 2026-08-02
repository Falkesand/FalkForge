using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder that accumulates the pieces of an installer package and assembles the
/// immutable <see cref="PackageModel"/> via <see cref="Build"/>. The fluent surface is split
/// across domain-focused partial files (Files, Services, Registry, Features, CustomActions,
/// ShellIntegration, Configuration); the accumulator fields and the <see cref="Build"/>
/// assembly live here.
/// </summary>
public sealed partial class PackageBuilder
{
    private readonly List<AssemblyModel> _assemblies = [];
    private readonly List<BinaryModel> _binaries = [];
    private readonly List<ComClassModel> _comClasses = [];
    private readonly List<ComTypeLibModel> _typeLibs = [];
    private readonly List<CreateFolderModel> _createFolders = [];
    private readonly List<CustomActionModel> _customActions = [];
    private readonly List<CustomTableModel> _customTables = [];
    private readonly List<DuplicateFileModel> _duplicateFiles = [];
    private readonly List<EnvironmentVariableModel> _environmentVariables = [];
    private readonly List<SequenceActionModel> _executeSequenceActions = [];
    private readonly List<FeatureModel> _features = [];
    private readonly List<FileAssociationModel> _fileAssociations = [];
    private readonly List<FileEntryModel> _files = [];
    private readonly List<FontModel> _fonts = [];
    private readonly List<IniFileModel> _iniFiles = [];
    private readonly List<LaunchConditionModel> _launchConditions = [];

    private readonly List<LocalizationData> _localizationData = [];
    private readonly List<MoveFileModel> _moveFiles = [];
    private readonly List<PermissionModel> _permissions = [];
    private readonly List<PropertyModel> _properties = [];
    private readonly List<RegistryEntryModel> _registryEntries = [];
    private readonly List<RemoveFileModel> _removeFiles = [];
    private readonly List<RemoveIniFileModel> _removeIniFiles = [];
    private readonly List<RemoveRegistryModel> _removeRegistryEntries = [];
    private readonly List<ServiceControlModel> _serviceControls = [];
    private readonly List<ServiceModel> _services = [];
    private readonly List<ShortcutModel> _shortcuts = [];
    private readonly List<SequenceActionModel> _uiSequenceActions = [];

    private MsiDialogSet _dialogSet = MsiDialogSet.None;
    private DialogCustomization? _dialogCustomization;
    private DowngradeModel? _downgrade;
    private IntegrityConfiguration? _integrity;
    private MajorUpgradeModel? _majorUpgrade;
    private MediaTemplateModel? _mediaTemplate;
    private ReproducibleBuildOptions? _reproducibleOptions;
    private SigningOptions? _signing;
    private UpgradeModel? _upgrade;

    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public Version Version { get; set; } = new(1, 0, 0);
    public Guid? UpgradeCode { get; set; }
    public Guid? ProductCode { get; set; }
    public InstallScope Scope { get; set; } = InstallScope.PerMachine;
    public ProcessorArchitecture Architecture { get; set; } = ProcessorArchitecture.X64;
    public InstallPath? DefaultInstallDirectory { get; set; }
    public CompressionLevel Compression { get; set; } = CompressionLevel.High;
    public string? Description { get; set; }
    public string? Comments { get; set; }
    public string? Contact { get; set; }
    public string? HelpUrl { get; set; }
    public string? AboutUrl { get; set; }
    public string? UpdateUrl { get; set; }
    public string? LicenseFile { get; set; }
    public string? ProductIconFile { get; set; }
    public bool EnableRestartManager { get; set; }

    private SbomOptions? _sbomOptions;
    private IceConfiguration? _iceConfiguration;
    private WinGetConfig? _winGet;

    public PackageModel Build()
    {
        var upgradeCode = UpgradeCode ??
                          GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace,
                              $"{Name}::{Manufacturer}");
        // Match PropertyTableProducer's ProductVersion for every version the compiler
        // accepts (major.minor.build): Windows Installer only ever reads three version
        // fields, so a 4th (Revision) component -- e.g. a CI build number in "1.0.0.100"
        // -- must not change product identity, or a rebuild that only bumps Revision gets
        // a new ProductCode while ProductVersion (and RemoveExistingProducts' VersionMax
        // match) stays identical, landing the install side by side instead of upgrading.
        // Build defaults to -1 for a 2-component Version (Version.ToString(3) throws in
        // that case); clamp it to 0 rather than let that turn a silent identity bug into
        // a crash inside the builder.
        var msiVersion = $"{Version.Major}.{Version.Minor}.{(Version.Build < 0 ? 0 : Version.Build)}";

        // Architecture and Scope both change compiled product identity: Architecture
        // drives the SummaryInformation Template (MsiRecipeBuilder.Metadata.cs) and the
        // Component 64-bit attribute bit (ComponentTableProducer.cs); Scope drives
        // ALLUSERS (PropertyTableProducer.cs). Without both in the key, an ordinary
        // dual-architecture ship -- same Name/Manufacturer/Version, built once for X86 and
        // once for X64 -- would derive the SAME ProductCode with a different Template, and
        // likewise for a PerMachine vs. PerUser build of the same product: installing the
        // second build then fails with 1638 (ERROR_PRODUCT_VERSION) instead of installing,
        // with no build-time error.
        var productCode = ProductCode ?? GuidUtility.CreateDeterministicGuid(
            GuidUtility.FalkForgeNamespace,
            $"{Name}::{Manufacturer}::{msiVersion}::{ArchitectureToken(Architecture)}::{ScopeToken(Scope)}");
        var defaultInstallDir = DefaultInstallDirectory ?? KnownFolder.ProgramFiles / Manufacturer / Name;

        // If no features defined, create implicit "Complete" feature
        var features = _features.Count > 0
            ? _features
            : [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }];

        // Reproducible mode: leave PackageCode null so the compiler derives it
        // from a content digest (ensuring different payloads → different codes).
        // Normal mode: capture a fresh GUID now so it is stable for the lifetime
        // of this PackageModel (e.g. if the model is compiled more than once in
        // the same process, all compilations share the same PackageCode).
        var packageCode = _reproducibleOptions is not null ? (Guid?)null : Guid.NewGuid();

        return new PackageModel
        {
            Name = Name,
            Manufacturer = Manufacturer,
            Version = Version,
            UpgradeCode = upgradeCode,
            ProductCode = productCode,
            PackageCode = packageCode,
            Scope = Scope,
            Architecture = Architecture,
            DefaultInstallDirectory = defaultInstallDir,
            Compression = Compression,
            Description = Description,
            Comments = Comments,
            Contact = Contact,
            HelpUrl = HelpUrl,
            AboutUrl = AboutUrl,
            UpdateUrl = UpdateUrl,
            LicenseFile = LicenseFile,
            ProductIcon = ProductIconFile,
            Files = _files,
            Features = features,
            Shortcuts = _shortcuts,
            Services = _services,
            ServiceControls = _serviceControls,
            RegistryEntries = _registryEntries,
            RemoveRegistryEntries = _removeRegistryEntries,
            EnvironmentVariables = _environmentVariables,
            Fonts = _fonts,
            Properties = _properties,
            LaunchConditions = _launchConditions,
            IniFiles = _iniFiles,
            RemoveIniFiles = _removeIniFiles,
            Permissions = _permissions,
            FileAssociations = _fileAssociations,
            CustomActions = _customActions,
            Binaries = _binaries,
            RemoveFiles = _removeFiles,
            CreateFolders = _createFolders,
            MoveFiles = _moveFiles,
            DuplicateFiles = _duplicateFiles,
            Assemblies = _assemblies,
            CustomTables = _customTables,
            ExecuteSequenceActions = _executeSequenceActions,
            UISequenceActions = _uiSequenceActions,
            MediaTemplate = _mediaTemplate,
            EnableRestartManager = EnableRestartManager,
            Signing = _signing,
            Upgrade = _upgrade ?? (_majorUpgrade is null ? new UpgradeModel() : null),
            MajorUpgrade = _majorUpgrade,
            Downgrade = _downgrade,
            DialogSet = _dialogSet,
            DialogCustomization = _dialogCustomization?.ToModel(),
            CustomDialogs = _customDialogs,
            LocalizationData = _localizationData,
            ReproducibleOptions = _reproducibleOptions,
            SbomOptions = _sbomOptions,
            IceConfiguration = _iceConfiguration,
            ComClasses = [.. _comClasses],
            TypeLibs = [.. _typeLibs],
            Integrity = _integrity,
            WinGet = _winGet
        };
    }

    // Rendered via an explicit switch rather than architecture.ToString(): the enum's
    // member names are an implementation detail of this codebase, so a future rename would
    // silently change every already-shipped ProductCode. The raw numeric value was rejected
    // for the same reason -- inserting a new member ahead of an existing one would renumber
    // it and shift every derived GUID. Used only to build the ProductCode key in Build().
    private static string ArchitectureToken(ProcessorArchitecture architecture) => architecture switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        ProcessorArchitecture.Arm64 => "arm64",
        _ => throw new ArgumentOutOfRangeException(nameof(architecture), architecture,
            "Unknown processor architecture."),
    };

    // Same rationale as ArchitectureToken: explicit switch, not scope.ToString(), so a
    // future enum-member rename cannot silently change an already-shipped ProductCode.
    private static string ScopeToken(InstallScope scope) => scope switch
    {
        InstallScope.PerMachine => "machine",
        InstallScope.PerUser => "user",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown install scope."),
    };
}
