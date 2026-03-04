namespace FalkForge.Engine.Protocol.Manifest;

public sealed class InstallerManifest
{
    public required string Name { get; init; }
    public required string Manufacturer { get; init; }
    public required string Version { get; init; }
    public required Guid BundleId { get; init; }
    public required Guid UpgradeCode { get; init; }
    public required PackageInfo[] Packages { get; init; }
    public RelatedBundleEntry[] RelatedBundles { get; init; } = [];
    public ManifestChainItem[] Chain { get; init; } = [];
    public ManifestVariable[] Variables { get; init; } = [];
    public ManifestFeature[] Features { get; init; } = [];
    public ManifestDependencyProvider[] DependencyProviders { get; init; } = [];
    public ManifestDependencyConsumer[] DependencyConsumers { get; init; } = [];
    public ManifestDependencyRequirement[] DependencyRequirements { get; init; } = [];
    public string? LicenseFile { get; init; }
    public ManifestUpdateFeed? UpdateFeed { get; init; }
    public required InstallScope Scope { get; init; }
}
