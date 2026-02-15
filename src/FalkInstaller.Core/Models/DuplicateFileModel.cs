namespace FalkInstaller.Models;

public sealed class DuplicateFileModel
{
    public required string Id { get; init; }
    public required string FileRef { get; init; }
    public string? DestDirectory { get; init; }
    public string? DestFileName { get; init; }
    public string? ComponentRef { get; init; }
}
