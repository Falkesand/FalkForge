namespace FalkInstaller.Compiler.Bundle;

public sealed class BundlePackageModel
{
    public required string Id { get; init; }
    public required BundlePackageType Type { get; init; }
    public required string DisplayName { get; init; }
    public string? Version { get; init; }
    public bool Vital { get; init; } = true;
    public required string SourcePath { get; init; }
    public Dictionary<string, string> Properties { get; init; } = new();
    public string? InstallCondition { get; init; }
}
