namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestUpdateFeed(string FeedUrl, UpdatePolicy Policy, bool AllowResumeDownload);
