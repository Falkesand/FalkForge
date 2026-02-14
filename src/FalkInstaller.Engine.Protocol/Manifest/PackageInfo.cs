namespace FalkInstaller.Engine.Protocol.Manifest;

public sealed class PackageInfo
{
    public required string Id { get; init; }
    public required PackageType Type { get; init; }
    public required string DisplayName { get; init; }
    public string? Version { get; init; }
    public bool Vital { get; init; } = true;
    public required string SourcePath { get; init; }
    public required string Sha256Hash { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
}
