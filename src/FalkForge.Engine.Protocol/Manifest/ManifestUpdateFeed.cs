namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestUpdateFeed(
    string FeedUrl,
    UpdatePolicy Policy,
    bool AllowResumeDownload,
    bool ShowDownloadProgress = true,
    bool ShowDownloadErrors = false,
    bool PromptBeforeAutoUpdate = false);
