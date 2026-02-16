namespace FalkForge.Extensions.Iis.Models;

public sealed class WebSiteModel
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public required string Directory { get; init; }
    public IReadOnlyList<WebBindingModel> Bindings { get; init; } = [];
    public string? AppPool { get; init; }
    public bool AutoStart { get; init; } = true;
    public int ConnectionTimeout { get; init; } = 120;
    public IReadOnlyList<WebApplicationModel> WebApplications { get; init; } = [];
}
