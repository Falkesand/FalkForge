namespace FalkForge.Models;

public sealed class PermissionModel
{
    public required string LockObject { get; init; }
    public required string Table { get; init; } // "File", "Registry", "CreateFolder"
    public string? Sddl { get; init; }
    public string? Domain { get; init; }
    public string? User { get; init; }
    public int Permission { get; init; }
    public string? FeatureRef { get; init; }
}
