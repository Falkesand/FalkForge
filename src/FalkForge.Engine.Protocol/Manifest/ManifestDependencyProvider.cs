namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestDependencyProvider(
    string Key,
    string Version,
    string? DisplayName);
