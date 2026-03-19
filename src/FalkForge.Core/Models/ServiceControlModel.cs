namespace FalkForge.Models;

public sealed class ServiceControlModel
{
    public required string Id { get; init; }
    public required string ServiceName { get; init; }
    public ServiceControlEvent Events { get; init; } = ServiceControlEvent.None;
    public bool Wait { get; init; } = true;
    public string? Arguments { get; init; }
    public string? ComponentRef { get; init; }
}