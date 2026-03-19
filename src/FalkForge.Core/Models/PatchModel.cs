namespace FalkForge.Models;

public sealed class PatchModel
{
    public required Guid Id { get; init; }
    public required PatchClassification Classification { get; init; }
    public string? Description { get; init; }
    public string? Manufacturer { get; init; }
    public Guid TargetProductCode { get; init; }
    public string? TargetVersion { get; init; }
    public string? UpdatedVersion { get; init; }
    public required string TargetMsiPath { get; init; }
    public required string UpdatedMsiPath { get; init; }
    public bool AllowRemoval { get; init; }
}