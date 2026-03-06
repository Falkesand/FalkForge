using FalkForge.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msix.Builders;

public sealed class MsixBuilder
{
    private readonly List<MsixApplication> _applications = [];
    private readonly List<FileEntryModel> _files = [];
    private readonly List<MsixRegistryEntry> _registryEntries = [];
    private readonly List<string> _capabilities = [];
    private readonly List<string> _restrictedCapabilities = [];
    private readonly List<MsixPackageDependency> _dependencies = [];
    private readonly List<MsixExtension> _extensions = [];
    private readonly List<VfsOverride> _vfsOverrides = [];
    private string _name = string.Empty;
    private string _publisher = string.Empty;
    private string _displayName = string.Empty;
    private string _publisherDisplayName = string.Empty;
    private Version _version = new(1, 0, 0, 0);
    private ProcessorArchitecture _architecture = ProcessorArchitecture.X64;
    private InstallScope _scope = InstallScope.PerMachine;
    private string? _description;
    private string? _logoPath;
    private string _minWindowsVersion = "10.0.17763.0";
    private string? _maxVersionTested;
    private VfsMappingMode _vfsMapping = VfsMappingMode.Auto;
    private SigningOptions? _signing;
    private MsixUpdateSettings? _updateSettings;

    public MsixBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public MsixBuilder Publisher(string publisher)
    {
        _publisher = publisher;
        return this;
    }

    public MsixBuilder DisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public MsixBuilder PublisherDisplayName(string name)
    {
        _publisherDisplayName = name;
        return this;
    }

    public MsixBuilder Version(Version version)
    {
        _version = version;
        return this;
    }

    public MsixBuilder Architecture(ProcessorArchitecture architecture)
    {
        _architecture = architecture;
        return this;
    }

    public MsixBuilder Scope(InstallScope scope)
    {
        _scope = scope;
        return this;
    }

    public MsixBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public MsixBuilder LogoPath(string path)
    {
        _logoPath = path;
        return this;
    }

    public MsixBuilder MinWindowsVersion(string version)
    {
        _minWindowsVersion = version;
        return this;
    }

    public MsixBuilder MaxVersionTested(string version)
    {
        _maxVersionTested = version;
        return this;
    }

    public MsixBuilder Application(string id, string executable, Action<MsixApplicationBuilder> configure)
    {
        var builder = new MsixApplicationBuilder(id, executable);
        configure(builder);
        _applications.Add(builder.Build());
        return this;
    }

    public MsixBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    public MsixBuilder RegistryEntry(string key, string? valueName = null, string? value = null,
        MsixRegistryValueType type = MsixRegistryValueType.String, string root = "HKCU")
    {
        _registryEntries.Add(new MsixRegistryEntry
        {
            Root = root,
            Key = key,
            ValueName = valueName,
            Value = value,
            Type = type
        });
        return this;
    }

    public MsixBuilder Signing(Action<SigningOptionsBuilder> configure)
    {
        var builder = new SigningOptionsBuilder();
        configure(builder);
        _signing = builder.Build();
        return this;
    }

    public MsixBuilder Capability(string capability)
    {
        _capabilities.Add(capability);
        return this;
    }

    public MsixBuilder RestrictedCapability(string capability)
    {
        _restrictedCapabilities.Add(capability);
        return this;
    }

    public MsixBuilder Dependency(string name, string publisher, Version? minVersion = null)
    {
        _dependencies.Add(new MsixPackageDependency
        {
            Name = name,
            Publisher = publisher,
            MinVersion = minVersion
        });
        return this;
    }

    public MsixBuilder Extension(string category, string? entryPoint = null)
    {
        _extensions.Add(new MsixExtension
        {
            Category = category,
            EntryPoint = entryPoint
        });
        return this;
    }

    public MsixBuilder VfsMapping(VfsMappingMode mode)
    {
        _vfsMapping = mode;
        return this;
    }

    public MsixBuilder VfsOverride(string sourceDir, string packageRelPath)
    {
        _vfsOverrides.Add(new VfsOverride
        {
            SourceDirectory = sourceDir,
            PackageRelativePath = packageRelPath
        });
        return this;
    }

    public MsixBuilder UpdateSettings(string appInstallerUri, Action<MsixUpdateSettingsBuilder>? configure = null)
    {
        var builder = new MsixUpdateSettingsBuilder(appInstallerUri);
        configure?.Invoke(builder);
        _updateSettings = builder.Build();
        return this;
    }

    public MsixModel Build() => new()
    {
        Name = _name,
        Publisher = _publisher,
        Version = _version,
        Architecture = _architecture,
        DisplayName = _displayName,
        PublisherDisplayName = _publisherDisplayName,
        Description = _description,
        LogoPath = _logoPath,
        Applications = _applications,
        Files = _files,
        RegistryEntries = _registryEntries,
        Capabilities = _capabilities,
        RestrictedCapabilities = _restrictedCapabilities,
        MinWindowsVersion = _minWindowsVersion,
        MaxVersionTested = _maxVersionTested,
        Dependencies = _dependencies,
        Extensions = _extensions,
        VfsMapping = _vfsMapping,
        VfsOverrides = _vfsOverrides,
        Scope = _scope,
        Signing = _signing,
        UpdateSettings = _updateSettings
    };
}
