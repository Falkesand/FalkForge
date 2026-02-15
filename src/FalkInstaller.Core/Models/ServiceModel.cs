namespace FalkInstaller.Models;

public sealed class ServiceModel
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Executable { get; init; }
    public string? Description { get; init; }
    public ServiceStartMode StartMode { get; init; } = ServiceStartMode.Automatic;
    public ServiceAccount Account { get; init; } = ServiceAccount.LocalSystem;
    public string? UserName { get; init; }
    public string? Password { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public IReadOnlyList<ServiceDependencyModel> TypedDependencies { get; init; } = [];
    public ServiceFailureActionsModel? FailureActions { get; init; }
    public string? FeatureRef { get; init; }
}
