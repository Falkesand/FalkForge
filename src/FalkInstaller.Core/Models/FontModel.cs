namespace FalkInstaller.Models;

public sealed class FontModel
{
    public required string FileName { get; init; }
    public string? FontTitle { get; init; }
    public string? FeatureRef { get; init; }
}
