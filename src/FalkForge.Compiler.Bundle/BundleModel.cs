namespace FalkForge.Compiler.Bundle;

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
    public BundleUiConfig? UiConfig { get; init; }
}
