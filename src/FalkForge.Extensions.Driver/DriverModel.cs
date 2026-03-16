namespace FalkForge.Extensions.Driver;

public sealed record DriverModel
{
    public required string Id { get; init; }
    public required string InfFilePath { get; init; }
    public DriverInstallFlags Flags { get; init; }
    public string? Description { get; init; }
    public string? Condition { get; init; }

    public bool ForceInstall => Flags.HasFlag(DriverInstallFlags.ForceInstall);
    public bool PlugAndPlay => Flags.HasFlag(DriverInstallFlags.PlugAndPlay);
}
