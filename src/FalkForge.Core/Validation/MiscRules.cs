using System.Collections.Frozen;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for miscellaneous model sections.
/// Split across partial files by responsibility:
/// <list type="bullet">
///   <item><see cref="MiscRules"/> — shared helpers (<c>ContainsSensitivePropertyReference</c>,
///     <c>ValidPermissionTables</c>) and the <see cref="All"/> array.</item>
///   <item>MiscRules.Shortcuts.cs — SHC001-003, FNT001, FAS001-003.</item>
///   <item>MiscRules.IniPermissions.cs — INI001-003, PRM001-004.</item>
///   <item>MiscRules.Registry.cs — REG007, RRG001-003.</item>
///   <item>MiscRules.FileOps.cs — RMF001-002, CRF001, MVF001-003, DPF001.</item>
/// </list>
/// </summary>
public static partial class MiscRules
{
    // ── Sensitive property detection ─────────────────────────────────────────
    // [GeneratedRegex] partial method must live in the same file as the partial
    // method declaration so the source generator can emit its implementation here.

    [GeneratedRegex(@"\[([A-Za-z_][A-Za-z0-9_.]*)\]", RegexOptions.None)]
    private static partial Regex MsiPropertyReferenceRegex();

    private static readonly FrozenSet<string> SensitiveKeywords =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
            "PASSWORD", "SECRET", "CREDENTIAL", "TOKEN", "APIKEY", "PASSPHRASE", "PIN");

    private static bool ContainsSensitivePropertyReference(string value)
    {
        foreach (Match match in MsiPropertyReferenceRegex().Matches(value))
        {
            var name = match.Groups[1].Value.ToUpperInvariant();
            foreach (var keyword in SensitiveKeywords)
                if (name.Contains(keyword))
                    return true;
        }
        return false;
    }

    // ── Permission table allow-list ───────────────────────────────────────────

    private static readonly FrozenSet<string> ValidPermissionTables =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
            "File", "Registry", "CreateFolder", "ServiceInstall");

    // ── All rules ─────────────────────────────────────────────────────────────

    /// <summary>
    /// All misc rules in order, ready to be included in a <see cref="RuleRegistry"/>.
    /// </summary>
    public static readonly ValidationRule[] All;

    /// <summary>
    /// Static constructor initializes <see cref="All"/> after all partial-class fields
    /// from the sibling files (MiscRules.Shortcuts.cs, MiscRules.IniPermissions.cs,
    /// MiscRules.Registry.cs, MiscRules.FileOps.cs) are guaranteed to be set.
    /// S3963 suppressed: the cross-partial static field references require a static
    /// constructor — the inline initializer form is not safe across partial files.
    /// </summary>
#pragma warning disable S3963 // Static fields initialized in static constructor (cross-partial dependency)
    static MiscRules()
    {
        All =
        [
            // Shortcuts / fonts / file associations  (MiscRules.Shortcuts.cs)
            Shc001_NameRequired,
            Shc002_TargetFileRequired,
            Shc003_LocationsWarning,
            Fnt001_FileNameRequired,
            Fas001_ExtensionRequired,
            Fas002_ProgIdRequired,
            Fas003_VerbsWarning,
            // INI files / permissions  (MiscRules.IniPermissions.cs)
            Ini001_FileNameRequired,
            Ini002_SectionRequired,
            Ini003_KeyRequired,
            Prm001_LockObjectRequired,
            Prm002_SddlOrUserRequired,
            Prm003_TableValid,
            Prm004_NoMixedPermissionTypes,
            // Registry  (MiscRules.Registry.cs)
            Reg007_SensitivePropertyInRegistry,
            Rrg001_IdRequired,
            Rrg002_KeyRequired,
            Rrg003_RemoveValueRequiresName,
            // File-system operations  (MiscRules.FileOps.cs)
            Rmf001_DirectoryRefRequired,
            Rmf002_InstallOrUninstallRequired,
            Crf001_DirectoryRefRequired,
            Mvf001_SourceDirectoryRequired,
            Mvf002_SourceFileNameRequired,
            Mvf003_DestDirectoryRequired,
            Dpf001_FileRefRequired
        ];
    }
}
