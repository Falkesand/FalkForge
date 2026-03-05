namespace FalkForge.Extensions.Iis.Models;

public sealed class WebVirtualDirectoryModel
{
    public required string Id { get; init; }
    public required string Alias { get; init; }
    public required string Directory { get; init; }
    public string? WebApplication { get; init; }
}