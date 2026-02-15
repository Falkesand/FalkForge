namespace FalkInstaller.Extensions.Iis.Models;

public sealed class WebApplicationModel
{
    public required string Id { get; init; }
    public required string Alias { get; init; }
    public required string Directory { get; init; }
    public string? AppPool { get; init; }
}
