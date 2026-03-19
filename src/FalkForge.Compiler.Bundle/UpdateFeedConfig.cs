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
}