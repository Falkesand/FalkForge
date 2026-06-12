using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle;

public sealed class UpdateFeedConfig
{
    public required string FeedUrl { get; init; }
    public UpdatePolicy Policy { get; init; } = UpdatePolicy.NotifyOnly;
    public bool AllowResumeDownload { get; init; } = true;
    public bool ShowDownloadProgress { get; init; } = true;
    public bool ShowDownloadErrors { get; init; }
    public bool PromptBeforeAutoUpdate { get; init; }

    /// <summary>
    /// Optional Authenticode certificate thumbprint (SHA-1, 40 hex characters) the engine
    /// pins for downloaded update bundles. When set, the engine's <c>DefaultUpdateLauncher</c>
    /// refuses to launch any update whose certificate thumbprint does not match exactly,
    /// preventing a compromised CA from substituting a differently-signed bundle.
    /// Flows to <c>InstallerManifest.UpdatePublisherThumbprint</c> via the manifest generator.
    /// Authored via <c>BundleBuilder.PinUpdatePublisher(thumbprint)</c>.
    /// </summary>
    public string? PublisherThumbprint { get; init; }
}