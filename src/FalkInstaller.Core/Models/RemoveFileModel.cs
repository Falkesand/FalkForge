namespace FalkInstaller.Models;

public sealed class RemoveFileModel
{
    public required string Id { get; init; }
    public required string DirectoryRef { get; init; }
    public string? FileName { get; init; }
    public bool OnInstall { get; init; }
    public bool OnUninstall { get; init; }
    public string? ComponentRef { get; init; }
}
