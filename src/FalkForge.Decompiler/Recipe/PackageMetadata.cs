namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Package-level metadata extracted from the MSI Property table.
/// Intermediate value produced by <see cref="MsiPackageReconstructor.ExtractMetadata"/>
/// before the full <see cref="PackageModel"/> is assembled.
/// </summary>
public sealed record PackageMetadata
{
    public required string Name { get; init; }
    public required string Manufacturer { get; init; }
    public required Version Version { get; init; }
    public Guid UpgradeCode { get; init; }
    public Guid ProductCode { get; init; }
    public InstallScope Scope { get; init; }
}
