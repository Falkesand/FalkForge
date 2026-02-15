namespace FalkInstaller.Extensions.Util.FileShare;

public sealed class FileShareModel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Directory { get; init; }
    public IReadOnlyList<FileSharePermission> Permissions { get; init; } = [];
}
