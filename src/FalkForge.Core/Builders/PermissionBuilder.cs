using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class PermissionBuilder
{
    private readonly string _lockObject;
    private string _table = "CreateFolder";

    internal PermissionBuilder(string lockObject)
    {
        _lockObject = lockObject;
    }

    public string? Sddl { get; set; }
    public string? Domain { get; set; }
    public string? User { get; set; }
    public int Permission { get; set; }

    public PermissionBuilder ForTable(string table)
    {
        _table = table;
        return this;
    }

    internal PermissionModel Build()
    {
        return new PermissionModel
        {
            LockObject = _lockObject,
            Table = _table,
            Sddl = Sddl,
            Domain = Domain,
            User = User,
            Permission = Permission
        };
    }
}