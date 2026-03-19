namespace FalkForge.Compiler.Bundle;

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
    public long MaxBytesPerSecond { get; init; }
    public SbomOptions? SbomOptions { get; init; }

    public IntegrityConfiguration? Integrity { get; init; }

    /// <summary>
    /// When true, the engine Apply phase simulates package execution
    /// instead of running installers.
    /// </summary>
    public bool IsDryRun { get; init; }
}
