namespace FalkForge.Extensions.Util.FileShare;

public sealed class FileSharePermission
{
    public required string User { get; init; }
    public required FileSharePermissionLevel Permission { get; init; }
}