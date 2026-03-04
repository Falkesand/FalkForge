using FalkForge.Builders;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Builders;

public sealed class BundleBuilder
{
    private string _name = string.Empty;
    private string _manufacturer = string.Empty;
    private string _version = "1.0.0";
    private Guid? _bundleId;
    private Guid? _upgradeCode;
    private InstallScope _scope = InstallScope.PerMachine;
    private ReproducibleBuildOptions? _reproducibleOptions;
    private readonly List<BundlePackageModel> _packages = new();
    private readonly List<ChainItem> _chainItems = new();
    private readonly List<RelatedBundleModel> _relatedBundles = new();
    private readonly List<ContainerModel> _containers = new();
    private readonly List<BundleVariableModel> _variables = new();
    private readonly List<BundleFeatureModel> _features = new();
    private readonly List<BundleDependencyProviderModel> _dependencyProviders = new();
    private readonly List<BundleDependencyConsumerModel> _dependencyConsumers = new();
    private readonly List<BundleDependencyRequirementModel> _dependencyRequirements = new();
    private BundleUiConfig? _uiConfig;
    private UpdateFeedConfig? _updateFeed;
    private int _containerCounter;
    private int _rollbackBoundaryCounter;

    public BundleBuilder Name(string name) { _name = name; return this; }
    public BundleBuilder Manufacturer(string manufacturer) { _manufacturer = manufacturer; return this; }
    public BundleBuilder Version(string version) { _version = version; return this; }
    public BundleBuilder BundleId(Guid id) { _bundleId = id; return this; }
    public BundleBuilder UpgradeCode(Guid code) { _upgradeCode = code; return this; }
    public BundleBuilder Scope(InstallScope scope) { _scope = scope; return this; }

    public BundleBuilder Reproducible(long? epochOverride = null)
    {
        long epoch;
        if (epochOverride.HasValue)
        {
            epoch = epochOverride.Value;
        }
        else if (Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH") is { } envValue)
        {
            if (!long.TryParse(envValue, out epoch))
                throw new ArgumentException($"RPR001: SOURCE_DATE_EPOCH '{envValue}' is not a valid Unix timestamp.");
        }
        else
        {
            throw new InvalidOperationException("RPR002: SOURCE_DATE_EPOCH is not set and no explicit epoch was provided.");
        }

        _reproducibleOptions = new ReproducibleBuildOptions { SourceDateEpoch = epoch };
        return this;
    }

    public BundleBuilder Chain(Action<ChainBuilder> configure)
    {
        var chain = new ChainBuilder();
        configure(chain);
        _packages.AddRange(chain.Build());
        _chainItems.AddRange(chain.BuildChain());
        return this;
    }

    public BundleBuilder UseBuiltInUI(
        string? licenseFile = null,
        string? logoFile = null,
        string? themeColor = null,
        string? watermarkImage = null,
        string? bannerImage = null,
        string? bannerIcon = null)
    {
        _uiConfig = new BundleUiConfig
        {
            UiType = BundleUiType.BuiltIn,
            LicenseFile = licenseFile,
            LogoFile = logoFile,
            ThemeColor = themeColor,
            WatermarkImage = watermarkImage,
            BannerImage = bannerImage,
            BannerIcon = bannerIcon
        };
        return this;
    }

    public BundleBuilder UseSilentUI()
    {
        _uiConfig = new BundleUiConfig { UiType = BundleUiType.Silent };
        return this;
    }

    public BundleBuilder UseCustomUI(string uiProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiProjectPath);
        _uiConfig = new BundleUiConfig
        {
            UiType = BundleUiType.Custom,
            CustomUiProjectPath = uiProjectPath
        };
        return this;
    }

    public BundleBuilder UpdateFeed(string feedUrl, UpdatePolicy policy = UpdatePolicy.NotifyOnly, bool allowResume = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);
        _updateFeed = new UpdateFeedConfig { FeedUrl = feedUrl, Policy = policy, AllowResumeDownload = allowResume };
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

    public ContainerRef DefineContainer(string id, Action<ContainerBuilder>? configure = null)
    {
        Container(id, configure);
        return new ContainerRef(id);
    }

    public ContainerRef DefineContainer(Action<ContainerBuilder>? configure = null)
    {
        var id = $"Container_{++_containerCounter}";
        Container(id, configure);
        return new ContainerRef(id);
    }

    /// <summary>
    /// Creates a rollback boundary reference with the specified id. Unlike <see cref="DefineContainer"/>,
    /// rollback boundaries are positional chain items registered when passed to
    /// <see cref="ChainBuilder.RollbackBoundary(RollbackBoundaryRef, Action{RollbackBoundaryBuilder}?)"/>.
    /// </summary>
    public RollbackBoundaryRef DefineRollbackBoundary(string id) => new(id);

    /// <summary>
    /// Creates a rollback boundary reference with an auto-generated id. Unlike <see cref="DefineContainer()"/>,
    /// rollback boundaries are positional chain items registered when passed to
    /// <see cref="ChainBuilder.RollbackBoundary(RollbackBoundaryRef, Action{RollbackBoundaryBuilder}?)"/>.
    /// </summary>
    public RollbackBoundaryRef DefineRollbackBoundary() =>
        new($"RollbackBoundary_{++_rollbackBoundaryCounter}");

    public BundleBuilder RelatedBundle(Guid bundleId, Action<RelatedBundleBuilder>? configure = null) =>
        RelatedBundle(bundleId.ToString("B").ToUpperInvariant(), configure);

    public BundleBuilder Variable(string name, Action<BundleVariableBuilder> configure)
    {
        var builder = new BundleVariableBuilder(name);
        configure(builder);
        _variables.Add(builder.Build());
        return this;
    }

    public BundleBuilder Feature(string id, Action<BundleFeatureBuilder> configure)
    {
        var builder = new BundleFeatureBuilder(id);
        configure(builder);
        _features.Add(builder.Build());
        return this;
    }

    public BundleBuilder DependencyProvider(string key, string version, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _dependencyProviders.Add(new BundleDependencyProviderModel
        {
            Key = key,
            Version = version,
            DisplayName = displayName
        });
        return this;
    }

    public BundleBuilder DependencyConsumer(string providerKey, string consumerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerKey);
        _dependencyConsumers.Add(new BundleDependencyConsumerModel
        {
            ProviderKey = providerKey,
            ConsumerKey = consumerKey
        });
        return this;
    }

    public BundleBuilder RequiresDependency(
        string providerKey,
        string? minVersion = null,
        string? maxVersion = null,
        bool minInclusive = true,
        bool maxInclusive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        _dependencyRequirements.Add(new BundleDependencyRequirementModel
        {
            ProviderKey = providerKey,
            MinVersion = minVersion,
            MaxVersion = maxVersion,
            MinInclusive = minInclusive,
            MaxInclusive = maxInclusive
        });
        return this;
    }

    public BundleModel Build()
    {
        var upgradeCode = _upgradeCode ?? (_reproducibleOptions is not null
            ? GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace, $"{_name}::{_manufacturer}")
            : Guid.NewGuid());

        var bundleId = _bundleId ?? (_reproducibleOptions is not null
            ? GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace, $"{_name}::{_manufacturer}::{_version}")
            : Guid.NewGuid());

        return new BundleModel
        {
            Name = _name,
            Manufacturer = _manufacturer,
            Version = _version,
            BundleId = bundleId,
            UpgradeCode = upgradeCode,
            Scope = _scope,
            Packages = _packages.AsReadOnly(),
            RelatedBundles = _relatedBundles.AsReadOnly(),
            Chain = _chainItems.AsReadOnly(),
            Variables = _variables.AsReadOnly(),
            Features = _features.AsReadOnly(),
            DependencyProviders = _dependencyProviders.AsReadOnly(),
            DependencyConsumers = _dependencyConsumers.AsReadOnly(),
            DependencyRequirements = _dependencyRequirements.AsReadOnly(),
            Containers = _containers.AsReadOnly(),
            UiConfig = _uiConfig,
            UpdateFeed = _updateFeed
        };
    }
}
