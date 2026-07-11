namespace FalkForge.Extensions.Util.UserManagement;

public sealed class UserModel
{
    public required string Name { get; init; }

    /// <summary>
    /// Literal account password. <b>Discouraged</b>: a literal here is embedded in plaintext in the
    /// compiled MSI (USR010 warning), mirroring the SQL015/IIS012/REG007/CTB011 posture. Prefer
    /// <see cref="PasswordProperty"/> with <c>SetSecureProperty</c>. Mutually exclusive with
    /// <see cref="PasswordProperty"/>.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Name of an MSI property that supplies the account password <b>at run time</b> — the secure,
    /// recommended path. The value is never stored in the MSI: the execution seam emits an immediate
    /// <c>SetProperty</c> (type 51) custom action that copies <c>[PasswordProperty]</c> into the deferred
    /// action's <c>CustomActionData</c>, and the value is supplied at run time via
    /// <c>IInstallerEngine.SetSecureProperty</c>. Mutually exclusive with <see cref="Password"/>.
    /// </summary>
    public string? PasswordProperty { get; init; }

    /// <summary>
    /// Optional domain qualifier. When <see langword="null"/>/empty the account is a <b>local</b> account
    /// created/updated via <c>New-LocalUser</c>/<c>Set-LocalUser</c>. When set, the account is treated as a
    /// pre-existing <b>domain</b> reference — it is never created (the local-account cmdlets cannot create
    /// domain principals); it is only referenced for group membership as <c>Domain\Name</c>.
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>Optional free-text description applied to a created/updated local user.</summary>
    public string? Description { get; init; }

    public bool CanNotChangePassword { get; init; }
    public bool Disabled { get; init; }
    public bool PasswordExpired { get; init; }
    public bool PasswordNeverExpires { get; init; }
    public bool RemoveOnUninstall { get; init; }
    public bool UpdateIfExists { get; init; }
    public string? ComponentRef { get; init; }

    /// <summary>
    /// Names of local groups the user is added to on install (<c>Add-LocalGroupMember</c>) and removed from
    /// on uninstall (<c>Remove-LocalGroupMember</c>). Adding a user to a privileged group (e.g.
    /// <c>Administrators</c>) is a deliberate authoring decision (privilege grant), so the names are
    /// validated but not blocked.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];
}
