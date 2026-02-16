namespace FalkForge.Extensions.Util.UserManagement;

public sealed class UserModel
{
    public required string Name { get; init; }
    public string? Password { get; init; }
    public string? Domain { get; init; }
    public bool CanNotChangePassword { get; init; }
    public bool Disabled { get; init; }
    public bool PasswordExpired { get; init; }
    public bool PasswordNeverExpires { get; init; }
    public bool RemoveOnUninstall { get; init; }
    public bool UpdateIfExists { get; init; }
    public string? ComponentRef { get; init; }
}
