namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class PackageBuilder
{
    private readonly List<FileEntryModel> _files = [];
    private readonly List<FeatureModel> _features = [];
    private readonly List<ShortcutModel> _shortcuts = [];
    private readonly List<ServiceModel> _services = [];
    private readonly List<RegistryEntryModel> _registryEntries = [];
    private readonly List<EnvironmentVariableModel> _environmentVariables = [];
    private readonly List<PropertyModel> _properties = [];
    private readonly List<LaunchConditionModel> _launchConditions = [];

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

    private UpgradeModel? _upgrade;

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
            RegistryEntries = _registryEntries,
            EnvironmentVariables = _environmentVariables,
            Properties = _properties,
            LaunchConditions = _launchConditions,
            Upgrade = _upgrade ?? new UpgradeModel()
        };
    }
}
