namespace FalkForge.Models;

public sealed class DirectoryModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ParentId { get; init; }
    public InstallPath? Path { get; init; }
}
