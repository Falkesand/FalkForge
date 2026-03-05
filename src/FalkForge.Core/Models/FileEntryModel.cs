namespace FalkForge.Models;

public sealed class FileEntryModel
{
    public required string SourcePath { get; init; }
    public required InstallPath TargetDirectory { get; init; }
    public required string FileName { get; init; }
    public bool IsKeyPath { get; init; }
    public string? ComponentId { get; init; }
    public Guid? ComponentGuid { get; init; }
    public string? FeatureRef { get; init; }
    public bool Vital { get; init; } = true;
    public string? ComponentCondition { get; init; }
}