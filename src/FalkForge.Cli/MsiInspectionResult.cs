namespace FalkForge.Cli;

/// <summary>
/// Contains metadata extracted from an MSI database during inspection.
/// </summary>
public sealed class MsiInspectionResult
{
    public string? ProductName { get; init; }
    public string? Manufacturer { get; init; }
    public string? Version { get; init; }
    public string? ProductCode { get; init; }
    public IReadOnlyList<string> TableNames { get; init; } = [];
    public int TableCount { get; init; }
}
