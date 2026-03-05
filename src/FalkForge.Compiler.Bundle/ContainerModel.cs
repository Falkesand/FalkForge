namespace FalkForge.Compiler.Bundle;

public sealed record ContainerModel
{
    public required string Id { get; init; }
    public string? DownloadUrl { get; init; }
}