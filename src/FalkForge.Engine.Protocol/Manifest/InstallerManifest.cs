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

    /// <summary>
    /// Optional Authenticode certificate thumbprint (SHA-1, hex) that the downloaded update
    /// bundle must present before the engine launches it. When non-null, the engine's
    /// <c>DefaultUpdateLauncher</c> passes this value to <c>IAuthenticodeValidator</c> and
    /// refuses to start any update whose certificate thumbprint does not match exactly.
    /// <para>
    /// Leave <c>null</c> (the default) to accept any valid Authenticode signature.
    /// Setting this field pins the publisher identity and prevents a compromised CA from
    /// issuing a certificate that passes signature verification but originates from a
    /// different publisher.
    /// </para>
    /// <para>
    /// Authored via <c>BundleBuilder.WithUpdateFeed(...).PinUpdatePublisher("&lt;thumbprint&gt;")</c>
    /// (authoring wiring not yet implemented — set the manifest field directly for now).
    /// </para>
    /// </summary>
    public string? UpdatePublisherThumbprint { get; init; }

    /// <summary>
    /// Prerequisite packages that the engine must detect and optionally install
    /// before spawning the managed WPF UI process.
    /// Empty array means no pre-UI prerequisites (the default; back-compat with older bundles).
    /// </summary>
    public PreUIPackageInfo[] PreUIPackages { get; init; } = [];
}
