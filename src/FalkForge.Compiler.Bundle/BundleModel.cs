namespace FalkForge.Compiler.Bundle;

using FalkForge.Builders;
using FalkForge.Compiler.Bundle.Models;
using FalkForge.Models;
using FalkForge.Sbom;

public sealed class BundleModel
{
    public required string Name { get; init; }
    public required string Manufacturer { get; init; }
    public required string Version { get; init; }
    public required Guid BundleId { get; init; }
    public required Guid UpgradeCode { get; init; }
    public required InstallScope Scope { get; init; }
    public required IReadOnlyList<BundlePackageModel> Packages { get; init; }
    public IReadOnlyList<RelatedBundleModel> RelatedBundles { get; init; } = [];
    public IReadOnlyList<ChainItem> Chain { get; init; } = [];
    public IReadOnlyList<ContainerModel> Containers { get; init; } = [];
    public IReadOnlyList<BundleVariableModel> Variables { get; init; } = [];
    public IReadOnlyList<BundleFeatureModel> Features { get; init; } = [];
    public IReadOnlyList<BundleDependencyProviderModel> DependencyProviders { get; init; } = [];
    public IReadOnlyList<BundleDependencyConsumerModel> DependencyConsumers { get; init; } = [];
    public IReadOnlyList<BundleDependencyRequirementModel> DependencyRequirements { get; init; } = [];
    public BundleUiConfig? UiConfig { get; init; }
    public UpdateFeedConfig? UpdateFeed { get; init; }

    /// <summary>
    /// Set by <see cref="Builders.BundleBuilder.Reproducible"/> to the resolved
    /// <see cref="ReproducibleBuildOptions.SourceDateEpoch"/> (explicit override or the
    /// <c>SOURCE_DATE_EPOCH</c> env var, resolved at build time). Mirrors
    /// <c>PackageModel.ReproducibleOptions</c> on the MSI side. Threaded into
    /// <see cref="ReproducibleSbomIdentity.Resolve"/> so an explicit epoch reaches the SBOM
    /// sidecar's serial/timestamp — not just the deterministic BundleId/UpgradeCode GUIDs.
    /// </summary>
    public ReproducibleBuildOptions? ReproducibleOptions { get; init; }

    /// <summary>
    /// Pinned Authenticode publisher thumbprint as authored via
    /// <see cref="Builders.BundleBuilder.PinUpdatePublisher"/>, carried independently of
    /// <see cref="UpdateFeed"/> so the validator can fail a pin that has no update feed to
    /// attach to (BDL032) rather than silently dropping it. When an update feed IS present the
    /// thumbprint is also merged into <see cref="UpdateFeedConfig.PublisherThumbprint"/>.
    /// </summary>
    public string? UpdatePublisherThumbprint { get; init; }
    public long MaxBytesPerSecond { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    public IntegrityConfiguration? Integrity { get; init; }

    /// <summary>
    /// When true, the engine Apply phase simulates package execution
    /// instead of running installers.
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Prerequisite packages installed by the NativeAOT engine before the managed WPF UI launches.
    /// Empty list means no pre-UI prerequisites (the default).
    /// </summary>
    public IReadOnlyList<PreUIPackageModel> PreUIPackages { get; init; } = [];

    /// <summary>
    /// Explicit opt-out from embedding the elevation companion
    /// (<c>FalkForge.Engine.Elevation.exe</c>) as a trust-covered payload. By default a runnable
    /// bundle (one embedding a real engine) carries the companion so per-machine (elevated)
    /// installs work from a lone distributed exe; a bundle authored per-user-only can opt out via
    /// <see cref="Builders.BundleBuilder.WithoutElevationCompanion"/> to save the payload bytes.
    /// The engine then falls back to per-user behavior instead of elevating.
    /// </summary>
    public bool OmitElevationCompanion { get; init; }
}
