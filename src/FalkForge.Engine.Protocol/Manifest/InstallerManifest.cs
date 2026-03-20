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
    public string? UiType { get; init; }
    public string? CustomUiProjectPath { get; init; }
    public string? LicenseFile { get; init; }
    public ManifestUpdateFeed? UpdateFeed { get; init; }
    public required InstallScope Scope { get; init; }
    public long MaxBytesPerSecond { get; init; }
    public bool IsDryRun { get; init; }
    public ManifestDryRunAction[] DryRunActions { get; init; } = [];
    public string[] UnsupportedExtensions { get; init; } = [];
    public string? ManifestSignature { get; init; }
    public string? SbomAttestation { get; init; }
    public bool IsDeltaUpdate { get; init; }
    public string? BaseVersion { get; init; }
    public string? BaseBundleSha256 { get; init; }
}
