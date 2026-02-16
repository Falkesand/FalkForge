namespace FalkForge.Extensions.Util.RemoveFolderEx;

public sealed class RemoveFolderExModel
{
    public required string Id { get; init; }
    public string? Directory { get; init; }
    public string? Property { get; init; }
    public required RemoveFolderExInstallMode InstallMode { get; init; }
}
