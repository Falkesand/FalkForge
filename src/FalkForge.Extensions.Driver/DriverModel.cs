namespace FalkForge.Extensions.Driver;

public sealed record DriverModel
{
    public required string Id { get; init; }
    public required string InfFilePath { get; init; }
    public bool ForceInstall { get; init; }
    public string? Condition { get; init; }
}
