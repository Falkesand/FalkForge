namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class PackageBuilder
{
    private readonly List<FileEntryModel> _files = [];
    private readonly List<FeatureModel> _features = [];
    private readonly List<ShortcutModel> _shortcuts = [];
    private readonly List<ServiceModel> _services = [];
    private readonly List<ServiceControlModel> _serviceControls = [];
    private readonly List<RegistryEntryModel> _registryEntries = [];
    private readonly List<RemoveRegistryModel> _removeRegistryEntries = [];
    private readonly List<EnvironmentVariableModel> _environmentVariables = [];
    private readonly List<FontModel> _fonts = [];
    private readonly List<PropertyModel> _properties = [];
    private readonly List<LaunchConditionModel> _launchConditions = [];
    private readonly List<IniFileModel> _iniFiles = [];
    private readonly List<PermissionModel> _permissions = [];
    private readonly List<FileAssociationModel> _fileAssociations = [];
    private readonly List<CustomActionModel> _customActions = [];
    private readonly List<BinaryModel> _binaries = [];
    private readonly List<RemoveFileModel> _removeFiles = [];
    private readonly List<CreateFolderModel> _createFolders = [];
    private readonly List<MoveFileModel> _moveFiles = [];
    private readonly List<DuplicateFileModel> _duplicateFiles = [];
    private readonly List<AssemblyModel> _assemblies = [];
    private readonly List<CustomTableModel> _customTables = [];
    private readonly List<SequenceActionModel> _executeSequenceActions = [];
    private readonly List<SequenceActionModel> _uiSequenceActions = [];
    private MediaTemplateModel? _mediaTemplate;

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

    private readonly List<LocalizationData> _localizationData = [];

    private MsiDialogSet _dialogSet = MsiDialogSet.None;
    private UpgradeModel? _upgrade;
    private MajorUpgradeModel? _majorUpgrade;
    private SigningOptions? _signing;

    public PackageBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    public ShortcutBuilder Shortcut(string name, string targetFile)
    {
        var builder = new ShortcutBuilder(name, targetFile, this);
        return builder;
    }

    public PackageBuilder Service(string name, Action<ServiceBuilder> configure)
    {
        var builder = new ServiceBuilder(name);
        configure(builder);
        _services.Add(builder.Build());
        return this;
    }

    public PackageBuilder ServiceControl(Action<ServiceControlBuilder> configure)
    {
        var builder = new ServiceControlBuilder();
        configure(builder);
        _serviceControls.Add(builder.Build());
        return this;
    }

    public PackageBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        _features.Add(builder.Build());
        return this;
    }

    public PackageBuilder Registry(Action<RegistryBuilder> configure)
    {
        var builder = new RegistryBuilder();
        configure(builder);
        _registryEntries.AddRange(builder.Build());
        return this;
    }

    public PackageBuilder RemoveRegistry(Action<RemoveRegistryBuilder> configure)
    {
        var builder = new RemoveRegistryBuilder();
        configure(builder);
        _removeRegistryEntries.Add(builder.Build());
        return this;
    }

    public PackageBuilder EnvironmentVariable(string name, string value, Action<EnvironmentVariableBuilder>? configure = null)
    {
        var builder = new EnvironmentVariableBuilder(name, value);
        configure?.Invoke(builder);
        _environmentVariables.Add(builder.Build());
        return this;
    }

    public PackageBuilder Property(string name, string value, Action<PropertyBuilder>? configure = null)
    {
        var builder = new PropertyBuilder(name, value);
        configure?.Invoke(builder);
        _properties.Add(builder.Build());
        return this;
    }

    public PackageBuilder Require(string condition, string message)
    {
        _launchConditions.Add(new LaunchConditionModel { Condition = condition, Message = message });
        return this;
    }

    public PackageBuilder Upgrade(Action<UpgradeBuilder> configure)
    {
        var builder = new UpgradeBuilder();
        configure(builder);
        _upgrade = builder.Build();
        return this;
    }

    public PackageBuilder MajorUpgrade(Action<MajorUpgradeBuilder> configure)
    {
        var builder = new MajorUpgradeBuilder();
        configure(builder);
        _majorUpgrade = builder.Build();
        return this;
    }

    public PackageBuilder Font(string fileName, Action<FontBuilder>? configure = null)
    {
        var builder = new FontBuilder(fileName);
        configure?.Invoke(builder);
        _fonts.Add(builder.Build());
        return this;
    }

    public PackageBuilder IniFile(string fileName, Action<IniFileBuilder> configure)
    {
        var builder = new IniFileBuilder(fileName);
        configure(builder);
        _iniFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder Permission(string lockObject, Action<PermissionBuilder> configure)
    {
        var builder = new PermissionBuilder(lockObject);
        configure(builder);
        _permissions.Add(builder.Build());
        return this;
    }

    public PackageBuilder FileAssociation(string extension, Action<FileAssociationBuilder> configure)
    {
        var builder = new FileAssociationBuilder(extension);
        configure(builder);
        _fileAssociations.Add(builder.Build());
        return this;
    }

    public PackageBuilder CustomAction(string id, Action<CustomActionBuilder> configure)
    {
        var builder = new CustomActionBuilder(id);
        configure(builder);
        _customActions.Add(builder.Build());
        return this;
    }

    public PackageBuilder RemoveFile(Action<RemoveFileBuilder> configure)
    {
        var builder = new RemoveFileBuilder();
        configure(builder);
        _removeFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder CreateFolder(Action<CreateFolderBuilder> configure)
    {
        var builder = new CreateFolderBuilder();
        configure(builder);
        _createFolders.Add(builder.Build());
        return this;
    }

    public PackageBuilder MoveFile(Action<MoveFileBuilder> configure)
    {
        var builder = new MoveFileBuilder();
        configure(builder);
        _moveFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder DuplicateFile(Action<DuplicateFileBuilder> configure)
    {
        var builder = new DuplicateFileBuilder();
        configure(builder);
        _duplicateFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder GacAssembly(Action<AssemblyBuilder> configure)
    {
        var builder = new AssemblyBuilder();
        configure(builder);
        _assemblies.Add(builder.Build());
        return this;
    }

    public PackageBuilder CustomTable(Action<CustomTableBuilder> configure)
    {
        var builder = new CustomTableBuilder();
        configure(builder);
        _customTables.Add(builder.Build());
        return this;
    }

    public PackageBuilder ExecuteSequence(Action<SequenceBuilder> configure)
    {
        var builder = new SequenceBuilder(SequenceTable.InstallExecuteSequence);
        configure(builder);
        var result = builder.Build();
        if (result.IsSuccess)
            _executeSequenceActions.AddRange(result.Value);
        return this;
    }

    public PackageBuilder UISequence(Action<SequenceBuilder> configure)
    {
        var builder = new SequenceBuilder(SequenceTable.InstallUISequence);
        configure(builder);
        var result = builder.Build();
        if (result.IsSuccess)
            _uiSequenceActions.AddRange(result.Value);
        return this;
    }

    public PackageBuilder MediaTemplate(Action<MediaTemplateBuilder> configure)
    {
        var builder = new MediaTemplateBuilder();
        configure(builder);
        _mediaTemplate = builder.Build();
        return this;
    }

    public PackageBuilder Binary(string name, string sourcePath)
    {
        _binaries.Add(new BinaryModel { Name = name, SourcePath = sourcePath });
        return this;
    }

    public PackageBuilder EnableRestartManagerSupport()
    {
        EnableRestartManager = true;
        return this;
    }

    public PackageBuilder CabinetThreads(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CabinetThreadCount = count;
        return this;
    }

    public PackageBuilder Signing(Action<SigningOptionsBuilder> configure)
    {
        var builder = new SigningOptionsBuilder();
        configure(builder);
        _signing = builder.Build();
        return this;
    }

    public PackageBuilder UseDialogSet(MsiDialogSet dialogSet)
    {
        _dialogSet = dialogSet;
        return this;
    }

    public PackageBuilder SetLocalizationData(IReadOnlyList<LocalizationData> data)
    {
        _localizationData.Clear();
        _localizationData.AddRange(data);
        return this;
    }

    internal void AddShortcut(ShortcutModel shortcut) => _shortcuts.Add(shortcut);

    public PackageModel Build()
    {
        var upgradeCode = UpgradeCode ?? GuidUtility.CreateDeterministicGuid(GuidUtility.FalkInstallerNamespace, $"{Name}::{Manufacturer}");
        var productCode = ProductCode ?? Guid.NewGuid();
        var defaultInstallDir = DefaultInstallDirectory ?? KnownFolder.ProgramFiles / Manufacturer / Name;

        // If no features defined, create implicit "Complete" feature
        var features = _features.Count > 0 ? _features : [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }];

        return new PackageModel
        {
            Name = Name,
            Manufacturer = Manufacturer,
            Version = Version,
            UpgradeCode = upgradeCode,
            ProductCode = productCode,
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
            DialogSet = _dialogSet,
            CabinetThreadCount = CabinetThreadCount,
            LocalizationData = _localizationData
        };
    }
}
