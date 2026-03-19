namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestFeature(
    string Id,
    string Title,
    string? Description,
    bool IsDefault,
    bool IsRequired,
    string[] PackageIds);
