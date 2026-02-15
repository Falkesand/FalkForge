namespace FalkInstaller.Extensions.Util.UserManagement;

public sealed class GroupModel
{
    public required string Name { get; init; }
    public string? Domain { get; init; }
    public string? ComponentRef { get; init; }
}
