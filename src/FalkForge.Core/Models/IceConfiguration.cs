namespace FalkForge.Models;

public sealed record IceConfiguration
{
    public bool Enabled { get; init; } = true;
    public string? CubFilePath { get; init; }
    public IReadOnlyList<string> SuppressedIces { get; init; } = [];
    public bool WarningsAsErrors { get; init; }
    public string? ReportPath { get; init; }
}
