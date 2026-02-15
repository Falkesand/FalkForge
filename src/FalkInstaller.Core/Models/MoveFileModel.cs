namespace FalkInstaller.Models;

public sealed class MoveFileModel
{
    public required string Id { get; init; }
    public required string SourceDirectory { get; init; }
    public required string SourceFileName { get; init; }
    public required string DestDirectory { get; init; }
    public string? DestFileName { get; init; }
    public int Options { get; init; } = 1;
    public string? ComponentRef { get; init; }
}
