namespace FalkForge.Models;

public sealed class CreateFolderModel
{
    public required string Id { get; init; }
    public required string DirectoryRef { get; init; }
    public string? ComponentRef { get; init; }
}
