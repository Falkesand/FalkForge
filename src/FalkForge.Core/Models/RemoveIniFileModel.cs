namespace FalkForge.Models;

/// <summary>
/// Authors an uninstall-time removal entry for the MSI <c>RemoveIniFile</c> table — distinct
/// from <see cref="IniFileModel"/>, which authors the <c>IniFile</c> table used for
/// install-time create/update entries. Per MSI semantics, uninstall-time INI cleanup belongs
/// in the dedicated <c>RemoveIniFile</c> table rather than the <c>IniFile</c> table.
/// </summary>
public sealed class RemoveIniFileModel
{
    /// <summary>Unique identifier for this row within the <c>RemoveIniFile</c> table.</summary>
    public required string Id { get; init; }

    public required string FileName { get; init; }
    public string? DirProperty { get; init; }
    public required string Section { get; init; }
    public required string Key { get; init; }
    public string? Value { get; init; }

    /// <summary>
    /// Governs whether a single line or an entire section is removed. Only
    /// <see cref="IniFileAction.RemoveLine"/> and <see cref="IniFileAction.RemoveTag"/> are
    /// meaningful for this table; defaults to <see cref="IniFileAction.RemoveLine"/>.
    /// </summary>
    public IniFileAction Action { get; init; } = IniFileAction.RemoveLine;

    /// <summary>
    /// Component this removal is tied to. When unset, the compiler falls back to the first
    /// resolved component (or a synthesized main component).
    /// </summary>
    public string? ComponentRef { get; init; }
}
