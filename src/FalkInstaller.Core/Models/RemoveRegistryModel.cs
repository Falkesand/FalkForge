namespace FalkInstaller.Models;

public sealed class RemoveRegistryModel
{
    public required string Id { get; init; }
    public RemoveRegistryAction Action { get; init; } = RemoveRegistryAction.RemoveKey;
    public required RegistryRoot Root { get; init; }
    public required string Key { get; init; }
    public string? Name { get; init; }
    public string? ComponentRef { get; init; }
}
