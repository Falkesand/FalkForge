namespace FalkForge.Models;

public sealed class FileAssociationModel
{
    public required string Extension { get; init; }
    public required string ProgId { get; init; }
    public string? Description { get; init; }
    public string? IconFile { get; init; }
    public int IconIndex { get; init; }
    public string? ContentType { get; init; }
    public IReadOnlyList<VerbModel> Verbs { get; init; } = [];
    public string? FeatureRef { get; init; }
}
