namespace FalkForge.Engine.Protocol.Manifest;

public sealed class ManifestDryRunAction
{
    public required string Kind { get; init; }
    public required string Description { get; init; }
}
