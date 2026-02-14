namespace FalkInstaller.Models;

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
    public IReadOnlyList<RegistryEntryModel> RegistryEntries { get; init; } = [];
    public IReadOnlyList<EnvironmentVariableModel> EnvironmentVariables { get; init; } = [];
    public UpgradeModel? Upgrade { get; init; }
    public IReadOnlyList<PropertyModel> Properties { get; init; } = [];
    public IReadOnlyList<FontModel> Fonts { get; init; } = [];
    public IReadOnlyList<LaunchConditionModel> LaunchConditions { get; init; } = [];
    public IReadOnlyList<IniFileModel> IniFiles { get; init; } = [];
    public IReadOnlyList<PermissionModel> Permissions { get; init; } = [];
    public IReadOnlyList<FileAssociationModel> FileAssociations { get; init; } = [];
    public IReadOnlyList<CustomActionModel> CustomActions { get; init; } = [];
    public IReadOnlyList<BinaryModel> Binaries { get; init; } = [];
    public bool EnableRestartManager { get; init; }
    public SigningOptions? Signing { get; init; }
}
