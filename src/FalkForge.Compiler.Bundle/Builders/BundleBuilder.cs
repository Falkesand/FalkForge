namespace FalkForge.Compiler.Bundle.Builders;

public sealed class BundleBuilder
{
    private string _name = string.Empty;
    private string _manufacturer = string.Empty;
    private string _version = "1.0.0";
    private Guid _bundleId = Guid.NewGuid();
    private Guid _upgradeCode = Guid.NewGuid();
    private InstallScope _scope = InstallScope.PerMachine;
    private readonly List<BundlePackageModel> _packages = new();
    private readonly List<ChainItem> _chainItems = new();
    private readonly List<RelatedBundleModel> _relatedBundles = new();
    private readonly List<ContainerModel> _containers = new();
    private BundleUiConfig? _uiConfig;

    public BundleBuilder Name(string name) { _name = name; return this; }
    public BundleBuilder Manufacturer(string manufacturer) { _manufacturer = manufacturer; return this; }
    public BundleBuilder Version(string version) { _version = version; return this; }
    public BundleBuilder BundleId(Guid id) { _bundleId = id; return this; }
    public BundleBuilder UpgradeCode(Guid code) { _upgradeCode = code; return this; }
    public BundleBuilder Scope(InstallScope scope) { _scope = scope; return this; }

    public BundleBuilder Chain(Action<ChainBuilder> configure)
    {
        var chain = new ChainBuilder();
        configure(chain);
        _packages.AddRange(chain.Build());
        _chainItems.AddRange(chain.BuildChain());
        return this;
    }

    public BundleBuilder UseBuiltInUI(string? licenseFile = null, string? logoFile = null, string? themeColor = null)
    {
        _uiConfig = new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn,
            LicenseFile = licenseFile,
            LogoFile = logoFile,
            ThemeColor = themeColor
        };
        return this;
    }

    public BundleBuilder UseSilentUI()
    {
        _uiConfig = new BundleUiConfig { UiType = BundleUiType.Silent };
        return this;
    }

    public BundleBuilder RelatedBundle(string bundleId, Action<RelatedBundleBuilder>? configure = null)
    {
        var builder = new RelatedBundleBuilder().BundleId(bundleId);
        configure?.Invoke(builder);
        _relatedBundles.Add(builder.Build());
        return this;
    }

    public BundleBuilder Container(string id, Action<ContainerBuilder>? configure = null)
    {
        var builder = new ContainerBuilder().Id(id);
        configure?.Invoke(builder);
        _containers.Add(builder.Build());
        return this;
    }

    public BundleModel Build()
    {
        return new BundleModel
        {
            Name = _name,
            Manufacturer = _manufacturer,
            Version = _version,
            BundleId = _bundleId,
            UpgradeCode = _upgradeCode,
            Scope = _scope,
            Packages = _packages.AsReadOnly(),
            RelatedBundles = _relatedBundles.AsReadOnly(),
            Chain = _chainItems.AsReadOnly(),
            Containers = _containers.AsReadOnly(),
            UiConfig = _uiConfig
        };
    }
}
