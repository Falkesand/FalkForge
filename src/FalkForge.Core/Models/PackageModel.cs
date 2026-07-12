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

    /// <summary>
    ///     MSI PackageCode (SummaryInformation PID 9 / RevisionNumber).
    ///     <para>
    ///     The MSI specification requires every non-identical package to carry a unique
    ///     PackageCode so that Windows Installer can distinguish cached packages at repair
    ///     time (SECREPAIR / KB2918614). ProductCode encodes product identity; PackageCode
    ///     encodes the specific package bytes.
    ///     </para>
    ///     <para>
    ///     <b>null</b> (the default) means "let the compiler decide":
    ///     <list type="bullet">
    ///       <item>Reproducible mode (<see cref="ReproducibleOptions"/> is set) — compiler
    ///         derives a deterministic GUID from a SHA-256 content digest of the resolved
    ///         files, product identity, and source-date epoch.</item>
    ///       <item>Normal mode — compiler assigns a fresh <see cref="Guid"/> each time the
    ///         recipe is built, guaranteeing uniqueness across independent build runs even
    ///         when <see cref="ProductCode"/> is pinned. When constructed via
    ///         <c>PackageBuilder.Build()</c>, the model carries <see langword="null"/> here
    ///         so the compiler always derives a new code per packaging event; reproducible
    ///         mode also leaves it <see langword="null"/> for content-digest derivation in
    ///         the compiler layer.</item>
    ///     </list>
    ///     </para>
    ///     <para>
    ///     Set an explicit value only when you need to pin the PackageCode for a known
    ///     binary-identical re-release (e.g. mirror distribution of a pre-built MSI).
    ///     </para>
    /// </summary>
    public Guid? PackageCode { get; init; }

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

    /// <summary>
    /// Optional path to an icon file surfaced in Add/Remove Programs via the
    /// <c>ARPPRODUCTICON</c> property. When set, the compiler adds the icon to
    /// the MSI <c>Icon</c> table and points <c>ARPPRODUCTICON</c> at that row.
    /// </summary>
    public string? ProductIcon { get; init; }
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
    public IReadOnlyList<RemoveIniFileModel> RemoveIniFiles { get; init; } = [];
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
