namespace FalkForge.Extensions.Util.UserManagement;

public sealed class GroupModel
{
    public required string Name { get; init; }

    /// <summary>
    /// Optional domain qualifier. When <see langword="null"/>/empty the group is a <b>local</b> group
    /// created via <c>New-LocalGroup</c>. When set, the group is treated as a pre-existing domain
    /// reference and is never created (the local-account cmdlets cannot create domain groups).
    /// </summary>
    public string? Domain { get; init; }

    /// <summary>Optional free-text description applied to a created local group.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// When <see langword="true"/> the group create step tolerates a pre-existing group (no rollback
    /// removal of a group that already existed). When <see langword="false"/> the group is expected to be
    /// created fresh, so a failed install rolls back by removing it.
    /// </summary>
    public bool UpdateIfExists { get; init; }

    /// <summary>When <see langword="true"/> the group is removed on uninstall (<c>Remove-LocalGroup</c>).</summary>
    public bool RemoveOnUninstall { get; init; }

    public string? ComponentRef { get; init; }
}
