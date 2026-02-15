namespace FalkForge.Models;

public sealed class ServiceDependencyModel
{
    public required string ServiceName { get; init; }
    public required string DependsOn { get; init; }
    public bool Group { get; init; }
}
