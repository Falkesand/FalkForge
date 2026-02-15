namespace FalkInstaller.Models;

public sealed class TransformModel
{
    public string? Id { get; init; }
    public required string BaseMsiPath { get; init; }
    public required string TargetMsiPath { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string> PropertyChanges { get; init; } = new Dictionary<string, string>();
}
