using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for miscellaneous model sections:
/// Shortcuts (SHC001-003), Fonts (FNT001), INI files (INI001-003),
/// Permissions (PRM001-004), File associations (FAS001-003),
/// Registry entries (REG007), Remove registry (RRG001-003),
/// Remove files (RMF001-002), Create folders (CRF001),
/// Move files (MVF001-003), Duplicate files (DPF001).
/// </summary>
public static partial class MiscRules
{
    // ── Sensitive property detection (shared by REG007) ──────────────────────

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

    private static readonly FrozenSet<string> ValidPermissionTables =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase,
            "File", "Registry", "CreateFolder", "ServiceInstall");

    // ── Shortcuts ────────────────────────────────────────────────────────────

    /// <summary>SHC001 — Shortcut Name is required.</summary>
    public static readonly ValidationRule Shc001_NameRequired = new(
        new RuleId("SHC001"),
        Severity.Error,
        ModelSection.Shortcut,
        "Shortcut Name required",
        "Every shortcut must have a non-empty Name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Shortcuts.Count; i++)
            {
                var s = ctx.Package.Shortcuts[i];
                if (string.IsNullOrWhiteSpace(s.Name))
                    violations.Add(new Violation(new RuleId("SHC001"), Severity.Error,
                        ModelPath.Root.Field("Shortcuts").Index(i).Field("Name"),
                        "Shortcut Name is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SHC002 — Shortcut TargetFile is required.</summary>
    public static readonly ValidationRule Shc002_TargetFileRequired = new(
        new RuleId("SHC002"),
        Severity.Error,
        ModelSection.Shortcut,
        "Shortcut TargetFile required",
        "Every shortcut must reference a non-empty TargetFile.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Shortcuts.Count; i++)
            {
                var s = ctx.Package.Shortcuts[i];
                if (string.IsNullOrWhiteSpace(s.TargetFile))
                    violations.Add(new Violation(new RuleId("SHC002"), Severity.Error,
                        ModelPath.Root.Field("Shortcuts").Index(i).Field("TargetFile"),
                        $"Shortcut '{s.Name}' must have a TargetFile."));
            }
            return violations.ToImmutable();
        });

    /// <summary>SHC003 — Shortcut has no locations (warning).</summary>
    public static readonly ValidationRule Shc003_LocationsWarning = new(
        new RuleId("SHC003"),
        Severity.Warning,
        ModelSection.Shortcut,
        "Shortcut has no locations",
        "A shortcut with no locations will not appear on the start menu or desktop.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Shortcuts.Count; i++)
            {
                var s = ctx.Package.Shortcuts[i];
                if (s.Locations.Count == 0)
                    violations.Add(new Violation(new RuleId("SHC003"), Severity.Warning,
                        ModelPath.Root.Field("Shortcuts").Index(i).Field("Locations"),
                        $"Shortcut '{s.Name}' has no locations specified."));
            }
            return violations.ToImmutable();
        });

    // ── Fonts ─────────────────────────────────────────────────────────────────

    /// <summary>FNT001 — Font FileName is required.</summary>
    public static readonly ValidationRule Fnt001_FileNameRequired = ValidationRule.Single(
        new RuleId("FNT001"),
        Severity.Error,
        ModelSection.Package,
        "Font FileName required",
        "Every font entry must have a non-empty FileName.",
        static ctx =>
        {
            for (var i = 0; i < ctx.Package.Fonts.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ctx.Package.Fonts[i].FileName))
                    return new Violation(new RuleId("FNT001"), Severity.Error,
                        ModelPath.Root.Field("Fonts").Index(i).Field("FileName"),
                        "Font FileName is required.");
            }
            return null;
        });

    // ── INI files ─────────────────────────────────────────────────────────────

    /// <summary>INI001 — INI file FileName is required.</summary>
    public static readonly ValidationRule Ini001_FileNameRequired = new(
        new RuleId("INI001"),
        Severity.Error,
        ModelSection.Package,
        "INI file FileName required",
        "Every INI file entry must specify a FileName.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.IniFiles.Count; i++)
            {
                var ini = ctx.Package.IniFiles[i];
                if (string.IsNullOrWhiteSpace(ini.FileName))
                    violations.Add(new Violation(new RuleId("INI001"), Severity.Error,
                        ModelPath.Root.Field("IniFiles").Index(i).Field("FileName"),
                        "INI file FileName is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>INI002 — INI file Section is required.</summary>
    public static readonly ValidationRule Ini002_SectionRequired = new(
        new RuleId("INI002"),
        Severity.Error,
        ModelSection.Package,
        "INI file Section required",
        "Every INI file entry must specify a Section.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.IniFiles.Count; i++)
            {
                var ini = ctx.Package.IniFiles[i];
                if (string.IsNullOrWhiteSpace(ini.Section))
                    violations.Add(new Violation(new RuleId("INI002"), Severity.Error,
                        ModelPath.Root.Field("IniFiles").Index(i).Field("Section"),
                        $"INI file '{ini.FileName}' must have a Section."));
            }
            return violations.ToImmutable();
        });

    /// <summary>INI003 — INI file Key is required.</summary>
    public static readonly ValidationRule Ini003_KeyRequired = new(
        new RuleId("INI003"),
        Severity.Error,
        ModelSection.Package,
        "INI file Key required",
        "Every INI file entry must specify a Key.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.IniFiles.Count; i++)
            {
                var ini = ctx.Package.IniFiles[i];
                if (string.IsNullOrWhiteSpace(ini.Key))
                    violations.Add(new Violation(new RuleId("INI003"), Severity.Error,
                        ModelPath.Root.Field("IniFiles").Index(i).Field("Key"),
                        $"INI file '{ini.FileName}' must have a Key."));
            }
            return violations.ToImmutable();
        });

    // ── Permissions ───────────────────────────────────────────────────────────

    /// <summary>PRM001 — Permission LockObject is required.</summary>
    public static readonly ValidationRule Prm001_LockObjectRequired = new(
        new RuleId("PRM001"),
        Severity.Error,
        ModelSection.Package,
        "Permission LockObject required",
        "Every permission entry must specify a LockObject.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Permissions.Count; i++)
            {
                var p = ctx.Package.Permissions[i];
                if (string.IsNullOrWhiteSpace(p.LockObject))
                    violations.Add(new Violation(new RuleId("PRM001"), Severity.Error,
                        ModelPath.Root.Field("Permissions").Index(i).Field("LockObject"),
                        "Permission LockObject is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>PRM002 — Permission must have either SDDL or User.</summary>
    public static readonly ValidationRule Prm002_SddlOrUserRequired = new(
        new RuleId("PRM002"),
        Severity.Error,
        ModelSection.Package,
        "Permission requires SDDL or User",
        "A permission entry must specify either an SDDL string or a User name.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Permissions.Count; i++)
            {
                var p = ctx.Package.Permissions[i];
                if (string.IsNullOrEmpty(p.Sddl) && string.IsNullOrEmpty(p.User))
                    violations.Add(new Violation(new RuleId("PRM002"), Severity.Error,
                        ModelPath.Root.Field("Permissions").Index(i),
                        $"Permission for '{p.LockObject}' must have either SDDL or User specified."));
            }
            return violations.ToImmutable();
        });

    /// <summary>PRM003 — Permission Table must be a valid MSI table name.</summary>
    public static readonly ValidationRule Prm003_TableValid = new(
        new RuleId("PRM003"),
        Severity.Error,
        ModelSection.Package,
        "Permission Table must be valid",
        "Permissions can only be applied to File, Registry, CreateFolder, or ServiceInstall tables.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Permissions.Count; i++)
            {
                var p = ctx.Package.Permissions[i];
                if (!string.IsNullOrEmpty(p.Table) && !ValidPermissionTables.Contains(p.Table))
                    violations.Add(new Violation(new RuleId("PRM003"), Severity.Error,
                        ModelPath.Root.Field("Permissions").Index(i).Field("Table"),
                        $"Permission for '{p.LockObject}' has invalid Table '{p.Table}'. Valid tables: File, Registry, CreateFolder, ServiceInstall."));
            }
            return violations.ToImmutable();
        });

    /// <summary>PRM004 — Cannot mix SDDL and User/Domain permissions in the same package.</summary>
    public static readonly ValidationRule Prm004_NoMixedPermissionTypes = ValidationRule.Single(
        new RuleId("PRM004"),
        Severity.Error,
        ModelSection.Package,
        "Cannot mix SDDL and User permissions",
        "MSI allows only one of LockPermissions (User/Domain) or MsiLockPermissionsEx (SDDL) per database.",
        static ctx =>
        {
            var hasSddl = false;
            var hasUser = false;
            foreach (var p in ctx.Package.Permissions)
            {
                if (!string.IsNullOrEmpty(p.Sddl)) hasSddl = true;
                else if (!string.IsNullOrEmpty(p.User)) hasUser = true;
            }
            return hasSddl && hasUser
                ? new Violation(new RuleId("PRM004"), Severity.Error,
                    ModelPath.Root.Field("Permissions"),
                    "Cannot mix SDDL permissions and User/Domain permissions in the same package. " +
                    "MSI allows only one of LockPermissions or MsiLockPermissionsEx per database.")
                : null;
        });

    // ── File associations ─────────────────────────────────────────────────────

    /// <summary>FAS001 — File association Extension is required.</summary>
    public static readonly ValidationRule Fas001_ExtensionRequired = new(
        new RuleId("FAS001"),
        Severity.Error,
        ModelSection.Package,
        "File association Extension required",
        "Every file association must specify a file extension.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.FileAssociations.Count; i++)
            {
                var a = ctx.Package.FileAssociations[i];
                if (string.IsNullOrWhiteSpace(a.Extension))
                    violations.Add(new Violation(new RuleId("FAS001"), Severity.Error,
                        ModelPath.Root.Field("FileAssociations").Index(i).Field("Extension"),
                        "File association Extension is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>FAS002 — File association ProgId is required.</summary>
    public static readonly ValidationRule Fas002_ProgIdRequired = new(
        new RuleId("FAS002"),
        Severity.Error,
        ModelSection.Package,
        "File association ProgId required",
        "Every file association must specify a ProgId.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.FileAssociations.Count; i++)
            {
                var a = ctx.Package.FileAssociations[i];
                if (string.IsNullOrWhiteSpace(a.ProgId))
                    violations.Add(new Violation(new RuleId("FAS002"), Severity.Error,
                        ModelPath.Root.Field("FileAssociations").Index(i).Field("ProgId"),
                        $"File association '{a.Extension}' must have a ProgId."));
            }
            return violations.ToImmutable();
        });

    /// <summary>FAS003 — File association has no verbs (warning).</summary>
    public static readonly ValidationRule Fas003_VerbsWarning = new(
        new RuleId("FAS003"),
        Severity.Warning,
        ModelSection.Package,
        "File association has no verbs",
        "A file association without verbs will not create any shell commands for the extension.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.FileAssociations.Count; i++)
            {
                var a = ctx.Package.FileAssociations[i];
                if (a.Verbs.Count == 0)
                    violations.Add(new Violation(new RuleId("FAS003"), Severity.Warning,
                        ModelPath.Root.Field("FileAssociations").Index(i).Field("Verbs"),
                        $"File association '{a.Extension}' has no verbs defined."));
            }
            return violations.ToImmutable();
        });

    // ── Registry ──────────────────────────────────────────────────────────────

    /// <summary>REG007 — Registry value references a sensitive MSI property (warning).</summary>
    public static readonly ValidationRule Reg007_SensitivePropertyInRegistry = new(
        new RuleId("REG007"),
        Severity.Warning,
        ModelSection.Registry,
        "Sensitive property in registry value",
        "Writing sensitive property values to the registry stores them in plaintext visible to any user or process with registry access.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RegistryEntries.Count; i++)
            {
                var entry = ctx.Package.RegistryEntries[i];
                if (entry.Value is not string stringValue)
                    continue;
                if (ContainsSensitivePropertyReference(stringValue))
                    violations.Add(new Violation(new RuleId("REG007"), Severity.Warning,
                        ModelPath.Root.Field("RegistryEntries").Index(i).Field("Value"),
                        $"Registry entry '{entry.Key}\\{entry.ValueName}' references a property that appears to contain sensitive data. " +
                        "Sensitive values written to the registry are stored in plaintext and visible to any user or process with registry access. " +
                        "Consider using a Windows service account or DPAPI-protected storage instead."));
            }
            return violations.ToImmutable();
        });

    // ── Remove registry ───────────────────────────────────────────────────────

    /// <summary>RRG001 — RemoveRegistry Id is required.</summary>
    public static readonly ValidationRule Rrg001_IdRequired = new(
        new RuleId("RRG001"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveRegistry Id required",
        "Every RemoveRegistry entry must have a non-empty Id.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RemoveRegistryEntries.Count; i++)
            {
                var e = ctx.Package.RemoveRegistryEntries[i];
                if (string.IsNullOrWhiteSpace(e.Id))
                    violations.Add(new Violation(new RuleId("RRG001"), Severity.Error,
                        ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Id"),
                        "RemoveRegistry Id is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>RRG002 — RemoveRegistry Key is required.</summary>
    public static readonly ValidationRule Rrg002_KeyRequired = new(
        new RuleId("RRG002"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveRegistry Key required",
        "Every RemoveRegistry entry must specify the registry key to remove.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RemoveRegistryEntries.Count; i++)
            {
                var e = ctx.Package.RemoveRegistryEntries[i];
                if (string.IsNullOrWhiteSpace(e.Key))
                    violations.Add(new Violation(new RuleId("RRG002"), Severity.Error,
                        ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Key"),
                        $"RemoveRegistry '{e.Id}' must have a Key."));
            }
            return violations.ToImmutable();
        });

    /// <summary>RRG003 — RemoveValue action requires a Name.</summary>
    public static readonly ValidationRule Rrg003_RemoveValueRequiresName = new(
        new RuleId("RRG003"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveValue requires Name",
        "When using the RemoveValue action, a value Name must be specified.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RemoveRegistryEntries.Count; i++)
            {
                var e = ctx.Package.RemoveRegistryEntries[i];
                if (e.Action == RemoveRegistryAction.RemoveValue && string.IsNullOrWhiteSpace(e.Name))
                    violations.Add(new Violation(new RuleId("RRG003"), Severity.Error,
                        ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Name"),
                        $"RemoveRegistry '{e.Id}' uses RemoveValue action but no Name specified."));
            }
            return violations.ToImmutable();
        });

    // ── Remove files ──────────────────────────────────────────────────────────

    /// <summary>RMF001 — RemoveFile DirectoryRef is required.</summary>
    public static readonly ValidationRule Rmf001_DirectoryRefRequired = new(
        new RuleId("RMF001"),
        Severity.Error,
        ModelSection.Package,
        "RemoveFile DirectoryRef required",
        "Every RemoveFile entry must specify a DirectoryRef.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RemoveFiles.Count; i++)
            {
                var rf = ctx.Package.RemoveFiles[i];
                if (string.IsNullOrWhiteSpace(rf.DirectoryRef))
                    violations.Add(new Violation(new RuleId("RMF001"), Severity.Error,
                        ModelPath.Root.Field("RemoveFiles").Index(i).Field("DirectoryRef"),
                        $"RemoveFile '{rf.Id}' must have a DirectoryRef."));
            }
            return violations.ToImmutable();
        });

    /// <summary>RMF002 — RemoveFile must specify at least OnInstall or OnUninstall.</summary>
    public static readonly ValidationRule Rmf002_InstallOrUninstallRequired = new(
        new RuleId("RMF002"),
        Severity.Error,
        ModelSection.Package,
        "RemoveFile install/uninstall trigger required",
        "A RemoveFile entry with neither OnInstall nor OnUninstall set will never execute.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.RemoveFiles.Count; i++)
            {
                var rf = ctx.Package.RemoveFiles[i];
                if (!rf.OnInstall && !rf.OnUninstall)
                    violations.Add(new Violation(new RuleId("RMF002"), Severity.Error,
                        ModelPath.Root.Field("RemoveFiles").Index(i),
                        $"RemoveFile '{rf.Id}' must specify at least one of OnInstall or OnUninstall."));
            }
            return violations.ToImmutable();
        });

    // ── Create folders ────────────────────────────────────────────────────────

    /// <summary>CRF001 — CreateFolder DirectoryRef is required.</summary>
    public static readonly ValidationRule Crf001_DirectoryRefRequired = new(
        new RuleId("CRF001"),
        Severity.Error,
        ModelSection.Package,
        "CreateFolder DirectoryRef required",
        "Every CreateFolder entry must specify a DirectoryRef.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CreateFolders.Count; i++)
            {
                var cf = ctx.Package.CreateFolders[i];
                if (string.IsNullOrWhiteSpace(cf.DirectoryRef))
                    violations.Add(new Violation(new RuleId("CRF001"), Severity.Error,
                        ModelPath.Root.Field("CreateFolders").Index(i).Field("DirectoryRef"),
                        $"CreateFolder '{cf.Id}' must have a DirectoryRef."));
            }
            return violations.ToImmutable();
        });

    // ── Move files ────────────────────────────────────────────────────────────

    /// <summary>MVF001 — MoveFile SourceDirectory is required.</summary>
    public static readonly ValidationRule Mvf001_SourceDirectoryRequired = new(
        new RuleId("MVF001"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile SourceDirectory required",
        "Every MoveFile entry must specify a SourceDirectory.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.MoveFiles.Count; i++)
            {
                var mf = ctx.Package.MoveFiles[i];
                if (string.IsNullOrWhiteSpace(mf.SourceDirectory))
                    violations.Add(new Violation(new RuleId("MVF001"), Severity.Error,
                        ModelPath.Root.Field("MoveFiles").Index(i).Field("SourceDirectory"),
                        $"MoveFile '{mf.Id}' must have a SourceDirectory."));
            }
            return violations.ToImmutable();
        });

    /// <summary>MVF002 — MoveFile SourceFileName is required.</summary>
    public static readonly ValidationRule Mvf002_SourceFileNameRequired = new(
        new RuleId("MVF002"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile SourceFileName required",
        "Every MoveFile entry must specify a SourceFileName.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.MoveFiles.Count; i++)
            {
                var mf = ctx.Package.MoveFiles[i];
                if (string.IsNullOrWhiteSpace(mf.SourceFileName))
                    violations.Add(new Violation(new RuleId("MVF002"), Severity.Error,
                        ModelPath.Root.Field("MoveFiles").Index(i).Field("SourceFileName"),
                        $"MoveFile '{mf.Id}' must have a SourceFileName."));
            }
            return violations.ToImmutable();
        });

    /// <summary>MVF003 — MoveFile DestDirectory is required.</summary>
    public static readonly ValidationRule Mvf003_DestDirectoryRequired = new(
        new RuleId("MVF003"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile DestDirectory required",
        "Every MoveFile entry must specify a DestDirectory.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.MoveFiles.Count; i++)
            {
                var mf = ctx.Package.MoveFiles[i];
                if (string.IsNullOrWhiteSpace(mf.DestDirectory))
                    violations.Add(new Violation(new RuleId("MVF003"), Severity.Error,
                        ModelPath.Root.Field("MoveFiles").Index(i).Field("DestDirectory"),
                        $"MoveFile '{mf.Id}' must have a DestDirectory."));
            }
            return violations.ToImmutable();
        });

    // ── Duplicate files ───────────────────────────────────────────────────────

    /// <summary>DPF001 — DuplicateFile FileRef is required.</summary>
    public static readonly ValidationRule Dpf001_FileRefRequired = new(
        new RuleId("DPF001"),
        Severity.Error,
        ModelSection.Package,
        "DuplicateFile FileRef required",
        "Every DuplicateFile entry must specify the source FileRef.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.DuplicateFiles.Count; i++)
            {
                var df = ctx.Package.DuplicateFiles[i];
                if (string.IsNullOrWhiteSpace(df.FileRef))
                    violations.Add(new Violation(new RuleId("DPF001"), Severity.Error,
                        ModelPath.Root.Field("DuplicateFiles").Index(i).Field("FileRef"),
                        $"DuplicateFile '{df.Id}' must have a FileRef."));
            }
            return violations.ToImmutable();
        });

    /// <summary>
    /// All misc rules in order, ready to be included in a <see cref="RuleRegistry"/>.
    /// </summary>
    public static readonly ValidationRule[] All =
    [
        Shc001_NameRequired,
        Shc002_TargetFileRequired,
        Shc003_LocationsWarning,
        Fnt001_FileNameRequired,
        Ini001_FileNameRequired,
        Ini002_SectionRequired,
        Ini003_KeyRequired,
        Prm001_LockObjectRequired,
        Prm002_SddlOrUserRequired,
        Prm003_TableValid,
        Prm004_NoMixedPermissionTypes,
        Fas001_ExtensionRequired,
        Fas002_ProgIdRequired,
        Fas003_VerbsWarning,
        Reg007_SensitivePropertyInRegistry,
        Rrg001_IdRequired,
        Rrg002_KeyRequired,
        Rrg003_RemoveValueRequiresName,
        Rmf001_DirectoryRefRequired,
        Rmf002_InstallOrUninstallRequired,
        Crf001_DirectoryRefRequired,
        Mvf001_SourceDirectoryRequired,
        Mvf002_SourceFileNameRequired,
        Mvf003_DestDirectoryRequired,
        Dpf001_FileRefRequired
    ];
}
