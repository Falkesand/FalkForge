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
    public bool EnableRestartManager { get; set; }
    public int CabinetThreadCount { get; set; }

    private SbomOptions? _sbomOptions;
    private IceConfiguration? _iceConfiguration;
    private WinGetConfig? _winGet;

    public PackageModel Build()
    {
        var upgradeCode = UpgradeCode ??
                          GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace,
                              $"{Name}::{Manufacturer}");
        var productCode = ProductCode ?? (_reproducibleOptions is not null
            ? GuidUtility.CreateDeterministicGuid(
                GuidUtility.FalkForgeNamespace,
                $"{Name}::{Manufacturer}::{Version}") // Version.ToString(): 2-component → "1.0", 3-component → "1.0.0"
            : Guid.NewGuid());
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
            CabinetThreadCount = CabinetThreadCount,
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
}
