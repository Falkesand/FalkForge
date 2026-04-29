using FalkForge.Builders;
using FalkForge.Sbom;

namespace FalkForge.Models;

public sealed class PackageModel
{
    public required string Name { get; init; }
    public required string Manufacturer { get; init; }
    public required Version Version { get; init; }
    public Guid UpgradeCode { get; init; }
    public Guid ProductCode { get; init; }
    public InstallScope Scope { get; init; } = InstallScope.PerMachine;
    public ProcessorArchitecture Architecture { get; init; } = ProcessorArchitecture.X64;
    public InstallPath? DefaultInstallDirectory { get; init; }
    public CompressionLevel Compression { get; init; } = CompressionLevel.High;
    public string? Description { get; init; }
    public string? Comments { get; init; }
    public string? Contact { get; init; }
    public string? HelpUrl { get; init; }
    public string? AboutUrl { get; init; }
    public string? UpdateUrl { get; init; }
    public string? LicenseFile { get; init; }
    public IReadOnlyList<FileEntryModel> Files { get; init; } = [];
    public IReadOnlyList<DirectoryModel> Directories { get; init; } = [];
    public IReadOnlyList<FeatureModel> Features { get; init; } = [];
    public IReadOnlyList<ShortcutModel> Shortcuts { get; init; } = [];
    public IReadOnlyList<ServiceModel> Services { get; init; } = [];
    public IReadOnlyList<ServiceControlModel> ServiceControls { get; init; } = [];
    public IReadOnlyList<RegistryEntryModel> RegistryEntries { get; init; } = [];
    public IReadOnlyList<RemoveRegistryModel> RemoveRegistryEntries { get; init; } = [];
    public IReadOnlyList<EnvironmentVariableModel> EnvironmentVariables { get; init; } = [];
    public UpgradeModel? Upgrade { get; init; }
    public MajorUpgradeModel? MajorUpgrade { get; init; }
    public DowngradeModel? Downgrade { get; init; }
    public IReadOnlyList<PropertyModel> Properties { get; init; } = [];
    public IReadOnlyList<FontModel> Fonts { get; init; } = [];
    public IReadOnlyList<LaunchConditionModel> LaunchConditions { get; init; } = [];
    public IReadOnlyList<IniFileModel> IniFiles { get; init; } = [];
    public IReadOnlyList<PermissionModel> Permissions { get; init; } = [];
    public IReadOnlyList<FileAssociationModel> FileAssociations { get; init; } = [];
    public IReadOnlyList<CustomActionModel> CustomActions { get; init; } = [];
    public IReadOnlyList<BinaryModel> Binaries { get; init; } = [];
    public IReadOnlyList<RemoveFileModel> RemoveFiles { get; init; } = [];
    public IReadOnlyList<CreateFolderModel> CreateFolders { get; init; } = [];
    public IReadOnlyList<MoveFileModel> MoveFiles { get; init; } = [];
    public IReadOnlyList<DuplicateFileModel> DuplicateFiles { get; init; } = [];
    public IReadOnlyList<AssemblyModel> Assemblies { get; init; } = [];
    public IReadOnlyList<CustomTableModel> CustomTables { get; init; } = [];
    public IReadOnlyList<SequenceActionModel> ExecuteSequenceActions { get; init; } = [];
    public IReadOnlyList<SequenceActionModel> UISequenceActions { get; init; } = [];
    public MediaTemplateModel? MediaTemplate { get; init; }
    public bool EnableRestartManager { get; init; }
    public SigningOptions? Signing { get; init; }
    public MsiDialogSet DialogSet { get; init; } = MsiDialogSet.None;
    public DialogCustomizationModel? DialogCustomization { get; init; }
    public int CabinetThreadCount { get; init; }
    public IReadOnlyList<LocalizationData> LocalizationData { get; init; } = [];
    public ReproducibleBuildOptions? ReproducibleOptions { get; init; }
    public SbomOptions? SbomOptions { get; init; }
    public IceConfiguration? IceConfiguration { get; init; }
    public IReadOnlyList<ComClassModel> ComClasses { get; init; } = [];
    public IReadOnlyList<ComTypeLibModel> TypeLibs { get; init; } = [];
    public IntegrityConfiguration? Integrity { get; init; }
    public WinGetConfig? WinGet { get; init; }
}
