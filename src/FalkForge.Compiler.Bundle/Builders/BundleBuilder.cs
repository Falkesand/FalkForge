using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Configuration;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Compiler.Bundle.Builders;

public sealed class BundleBuilder
{
    private readonly List<ChainItem> _chainItems = new();
    private readonly List<ContainerModel> _containers = new();
    private readonly List<BundleDependencyConsumerModel> _dependencyConsumers = new();
    private readonly List<BundleDependencyProviderModel> _dependencyProviders = new();
    private readonly List<BundleDependencyRequirementModel> _dependencyRequirements = new();
    private readonly List<BundleFeatureModel> _features = new();
    private readonly List<BundlePackageModel> _packages = new();
    private readonly List<RelatedBundleModel> _relatedBundles = new();
    private readonly List<BundleVariableModel> _variables = new();
    private readonly List<PreUIPackageModel> _preUIPackages = new();
    private Guid? _bundleId;
    private long _maxBytesPerSecond;
    private int _containerCounter;
    private string _manufacturer = string.Empty;
    private string _name = string.Empty;
    private ReproducibleBuildOptions? _reproducibleOptions;
    private int _rollbackBoundaryCounter;
    private InstallScope _scope = InstallScope.PerMachine;
    private BundleUiConfig? _uiConfig;
    private UpdateFeedConfig? _updateFeed;
    private string? _updatePublisherThumbprint;
    private Guid? _upgradeCode;
    private string _version = "1.0.0";
    private SbomOptions? _sbomOptions;
    private IntegrityConfiguration? _integrity;
    private bool _isDryRun;
    private string? _deltaBaseBundlePath;
    private bool _omitElevationCompanion;

    public BundleBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public BundleBuilder Manufacturer(string manufacturer)
    {
        _manufacturer = manufacturer;
        return this;
    }

    public BundleBuilder Version(string version)
    {
        _version = version;
        return this;
    }

    public BundleBuilder BundleId(Guid id)
    {
        _bundleId = id;
        return this;
    }

    public BundleBuilder UpgradeCode(Guid code)
    {
        _upgradeCode = code;
        return this;
    }

    public BundleBuilder Scope(InstallScope scope)
    {
        _scope = scope;
        return this;
    }

    public BundleBuilder Reproducible(long? epochOverride = null)
    {
        long epoch;
        if (epochOverride.HasValue)
        {
            epoch = epochOverride.Value;
        }
        else
        {
            var lookup = EnvVarCatalog.TryGetSourceDateEpoch();
            if (lookup.IsFailure)
                throw new ArgumentException(lookup.Error.Message);
            if (!lookup.Value.IsSet)
                throw new InvalidOperationException(
                    "RPR002: SOURCE_DATE_EPOCH is not set and no explicit epoch was provided.");
            epoch = lookup.Value.Value;
        }

        _reproducibleOptions = new ReproducibleBuildOptions { SourceDateEpoch = epoch };
        return this;
    }

    public BundleBuilder DownloadThrottle(long bytesPerSecond) { _maxBytesPerSecond = bytesPerSecond; return this; }

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

    /// <param name="feedUrl">Update feed URL.</param>
    /// <param name="policy">Whether updates are notify-only, prompted, or auto-downloaded.</param>
    /// <param name="allowResume">Whether interrupted downloads may resume from a partial file.</param>
    /// <param name="showDownloadProgress">Whether the UI shows a download progress indicator.</param>
    /// <param name="showDownloadErrors">Whether download failures are surfaced to the user.</param>
    /// <param name="promptBeforeAutoUpdate">
    /// Whether an <see cref="UpdatePolicy.AutoUpdate"/> feed still prompts the user before
    /// launching the downloaded update, rather than applying it silently.
    /// </param>
    public BundleBuilder UpdateFeed(string feedUrl, UpdatePolicy policy = UpdatePolicy.NotifyOnly,
        bool allowResume = true, bool showDownloadProgress = true, bool showDownloadErrors = false,
        bool promptBeforeAutoUpdate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedUrl);
        _updateFeed = new UpdateFeedConfig
        {
            FeedUrl = feedUrl,
            Policy = policy,
            AllowResumeDownload = allowResume,
            ShowDownloadProgress = showDownloadProgress,
            ShowDownloadErrors = showDownloadErrors,
            PromptBeforeAutoUpdate = promptBeforeAutoUpdate
        };
        return this;
    }

    /// <summary>
    /// Pins the Authenticode publisher thumbprint (SHA-1, 40 hex characters) that the engine
    /// must verify on a downloaded update bundle before launching it. A mismatch aborts the
    /// launch as a security error. Requires an update feed to be configured via
    /// <see cref="UpdateFeed"/>; the thumbprint is carried on the update feed config and
    /// surfaces in the manifest as <c>UpdatePublisherThumbprint</c>.
    /// </summary>
    /// <param name="thumbprint">The expected certificate thumbprint (40 hex characters).</param>
    public BundleBuilder PinUpdatePublisher(string thumbprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbprint);
        _updatePublisherThumbprint = thumbprint;
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
    ///     Creates a rollback boundary reference with the specified id. Unlike <see cref="DefineContainer" />,
    ///     rollback boundaries are positional chain items registered when passed to
    ///     <see cref="ChainBuilder.RollbackBoundary(RollbackBoundaryRef, Action{RollbackBoundaryBuilder}?)" />.
    /// </summary>
#pragma warning disable CA1822 // Intentional instance method for API consistency
    public RollbackBoundaryRef DefineRollbackBoundary(string id) => new(id);
#pragma warning restore CA1822

    /// <summary>
    ///     Creates a rollback boundary reference with an auto-generated id. Unlike <see cref="DefineContainer()" />,
    ///     rollback boundaries are positional chain items registered when passed to
    ///     <see cref="ChainBuilder.RollbackBoundary(RollbackBoundaryRef, Action{RollbackBoundaryBuilder}?)" />.
    /// </summary>
    public RollbackBoundaryRef DefineRollbackBoundary()
    {
        return new RollbackBoundaryRef($"RollbackBoundary_{++_rollbackBoundaryCounter}");
    }

    public BundleBuilder RelatedBundle(Guid bundleId, Action<RelatedBundleBuilder>? configure = null)
    {
        return RelatedBundle(bundleId.ToString("B").ToUpperInvariant(), configure);
    }

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

    public BundleBuilder Sbom(Action<SbomOptions>? configure = null)
    {
        _sbomOptions ??= new SbomOptions();
        configure?.Invoke(_sbomOptions);
        return this;
    }

    public BundleBuilder Integrity(Action<IntegrityBuilder> configure)
    {
        var builder = new IntegrityBuilder();
        configure(builder);
        _integrity = builder.Build();
        return this;
    }

    /// <summary>
    /// Configures delta bundle compilation against a previous bundle version.
    /// When set, <see cref="FalkForge.Compiler.Bundle.Compilation.DeltaBundleCompiler"/>
    /// is used to produce a smaller update bundle containing only binary deltas.
    /// </summary>
    public BundleBuilder DeltaFrom(string oldBundlePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldBundlePath);
        _deltaBaseBundlePath = oldBundlePath;
        return this;
    }

    /// <summary>
    /// Gets the path to the old bundle for delta compilation, or null for full builds.
    /// </summary>
    internal string? DeltaBaseBundlePath => _deltaBaseBundlePath;

    /// <summary>
    /// Registers a prerequisite that the NativeAOT engine must detect and optionally install
    /// <em>before</em> the managed WPF UI process is spawned.
    /// Use this for runtime dependencies (e.g., .NET 10 Desktop Runtime) that the UI exe itself
    /// requires — i.e., dependencies the managed host cannot satisfy without a pre-UI install step.
    /// </summary>
    /// <param name="sourcePath">
    /// Path to the installer executable on the build machine.
    /// Pass an empty string for remote-only payloads (call <see cref="PreUIPackageBuilder.RemotePayload"/> inside <paramref name="configure"/>).
    /// </param>
    /// <param name="configure">Fluent configuration of the prerequisite (Id, DisplayName, Arguments, SearchConditions, etc.).</param>
    public BundleBuilder PreUIPrerequisite(string sourcePath, Action<PreUIPackageBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PreUIPackageBuilder(sourcePath);
        configure(builder);
        _preUIPackages.Add(builder.Build());
        return this;
    }

    /// <summary>
    /// Opts this bundle out of embedding the elevation companion
    /// (<c>FalkForge.Engine.Elevation.exe</c>). By default a runnable bundle carries the companion
    /// as a trust-covered payload so per-machine (elevated) installs work from a lone distributed
    /// exe. Call this for a bundle authored per-user-only to save the payload bytes; the engine
    /// then falls back to per-user behavior instead of elevating.
    /// </summary>
    public BundleBuilder WithoutElevationCompanion()
    {
        _omitElevationCompanion = true;
        return this;
    }

    /// <summary>
    /// Marks this bundle as a dry-run installer. The engine Apply phase will
    /// simulate package execution instead of running real installers.
    /// </summary>
    public BundleBuilder DryRun()
    {
        _isDryRun = true;
        return this;
    }

    public BundleModel Build()
    {
        var upgradeCode = _upgradeCode ?? (_reproducibleOptions is not null
            ? GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace, $"{_name}::{_manufacturer}")
            : Guid.NewGuid());

        var bundleId = _bundleId ?? (_reproducibleOptions is not null
            ? GuidUtility.CreateDeterministicGuid(GuidUtility.FalkForgeNamespace,
                $"{_name}::{_manufacturer}::{_version}")
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
            UpdateFeed = _updatePublisherThumbprint is not null && _updateFeed is not null
                ? new UpdateFeedConfig
                {
                    FeedUrl = _updateFeed.FeedUrl,
                    Policy = _updateFeed.Policy,
                    AllowResumeDownload = _updateFeed.AllowResumeDownload,
                    ShowDownloadProgress = _updateFeed.ShowDownloadProgress,
                    ShowDownloadErrors = _updateFeed.ShowDownloadErrors,
                    PromptBeforeAutoUpdate = _updateFeed.PromptBeforeAutoUpdate,
                    PublisherThumbprint = _updatePublisherThumbprint
                }
                : _updateFeed,
            // Carry the pin independently of the feed so a pin without an update feed survives to
            // the validator (BDL032) instead of being silently dropped.
            UpdatePublisherThumbprint = _updatePublisherThumbprint,
            MaxBytesPerSecond = _maxBytesPerSecond,
            SbomOptions = _sbomOptions,
            Integrity = _integrity,
            IsDryRun = _isDryRun,
            PreUIPackages = _preUIPackages.AsReadOnly(),
            OmitElevationCompanion = _omitElevationCompanion
        };
    }
}