namespace FalkForge.Engine.Protocol.Manifest;

public sealed record ManifestVariable(
    string Name,
    string Type,
    string? DefaultValue,
    bool Persisted,
    bool Hidden,
    bool Secret
);
