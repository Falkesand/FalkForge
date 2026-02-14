namespace FalkInstaller.Models;

public sealed class EnvironmentVariableModel
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public bool IsSystem { get; init; } = true;
    public EnvironmentVariableAction Action { get; init; } = EnvironmentVariableAction.Set;
    public string? Part { get; init; }
    public string? Separator { get; init; }
    public string? FeatureRef { get; init; }
}
