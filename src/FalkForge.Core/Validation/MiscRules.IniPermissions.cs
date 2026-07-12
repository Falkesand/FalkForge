using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class MiscRules
{
    // ── INI files ─────────────────────────────────────────────────────────────

    /// <summary>INI001 — INI file FileName is required.</summary>
    public static readonly ValidationRule Ini001_FileNameRequired = new(
        new RuleId("INI001"),
        Severity.Error,
        ModelSection.Package,
        "INI file FileName required",
        "Every INI file entry must specify a FileName.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.IniFiles,
            static (ini, i) => string.IsNullOrWhiteSpace(ini.FileName)
                ? new Violation(new RuleId("INI001"), Severity.Error,
                    ModelPath.Root.Field("IniFiles").Index(i).Field("FileName"),
                    "INI file FileName is required.")
                : null));

    /// <summary>INI002 — INI file Section is required.</summary>
    public static readonly ValidationRule Ini002_SectionRequired = new(
        new RuleId("INI002"),
        Severity.Error,
        ModelSection.Package,
        "INI file Section required",
        "Every INI file entry must specify a Section.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.IniFiles,
            static (ini, i) => string.IsNullOrWhiteSpace(ini.Section)
                ? new Violation(new RuleId("INI002"), Severity.Error,
                    ModelPath.Root.Field("IniFiles").Index(i).Field("Section"),
                    $"INI file '{ini.FileName}' must have a Section.")
                : null));

    /// <summary>INI003 — INI file Key is required.</summary>
    public static readonly ValidationRule Ini003_KeyRequired = new(
        new RuleId("INI003"),
        Severity.Error,
        ModelSection.Package,
        "INI file Key required",
        "Every INI file entry must specify a Key.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.IniFiles,
            static (ini, i) => string.IsNullOrWhiteSpace(ini.Key)
                ? new Violation(new RuleId("INI003"), Severity.Error,
                    ModelPath.Root.Field("IniFiles").Index(i).Field("Key"),
                    $"INI file '{ini.FileName}' must have a Key.")
                : null));

    // ── Permissions ───────────────────────────────────────────────────────────

    /// <summary>PRM001 — Permission LockObject is required.</summary>
    public static readonly ValidationRule Prm001_LockObjectRequired = new(
        new RuleId("PRM001"),
        Severity.Error,
        ModelSection.Package,
        "Permission LockObject required",
        "Every permission entry must specify a LockObject.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Permissions,
            static (p, i) => string.IsNullOrWhiteSpace(p.LockObject)
                ? new Violation(new RuleId("PRM001"), Severity.Error,
                    ModelPath.Root.Field("Permissions").Index(i).Field("LockObject"),
                    "Permission LockObject is required.")
                : null));

    /// <summary>PRM002 — Permission must have either SDDL or User.</summary>
    public static readonly ValidationRule Prm002_SddlOrUserRequired = new(
        new RuleId("PRM002"),
        Severity.Error,
        ModelSection.Package,
        "Permission requires SDDL or User",
        "A permission entry must specify either an SDDL string or a User name.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Permissions,
            static (p, i) => string.IsNullOrEmpty(p.Sddl) && string.IsNullOrEmpty(p.User)
                ? new Violation(new RuleId("PRM002"), Severity.Error,
                    ModelPath.Root.Field("Permissions").Index(i),
                    $"Permission for '{p.LockObject}' must have either SDDL or User specified.")
                : null));

    /// <summary>PRM003 — Permission Table must be a valid MSI table name.</summary>
    public static readonly ValidationRule Prm003_TableValid = new(
        new RuleId("PRM003"),
        Severity.Error,
        ModelSection.Package,
        "Permission Table must be valid",
        "Permissions can only be applied to File, Registry, CreateFolder, or ServiceInstall tables.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Permissions,
            static (p, i) => !string.IsNullOrEmpty(p.Table) && !ValidPermissionTables.Contains(p.Table)
                ? new Violation(new RuleId("PRM003"), Severity.Error,
                    ModelPath.Root.Field("Permissions").Index(i).Field("Table"),
                    $"Permission for '{p.LockObject}' has invalid Table '{p.Table}'. Valid tables: File, Registry, CreateFolder, ServiceInstall.")
                : null));

    /// <summary>
    /// PRM005 — A non-SDDL (User/Domain) permission cannot be feature-gated. The MSI
    /// <c>LockPermissions</c> table (where User/Domain-driven permissions land) has no
    /// <c>Condition</c> or <c>Component_</c>/<c>Feature_</c> column, so a FeatureRef declared via
    /// <c>FeatureBuilder.Permission(...)</c> on such an entry has no honest compiled
    /// representation. Only SDDL-driven permissions (routed to <c>MsiLockPermissionsEx</c>, which
    /// does have a <c>Condition</c> column) can be feature-gated.
    /// </summary>
    public static readonly ValidationRule Prm005_FeatureRefRequiresSddl = new(
        new RuleId("PRM005"),
        Severity.Error,
        ModelSection.Package,
        "Feature-gated permission requires SDDL",
        "A permission entry with a FeatureRef must use SDDL — MSI's LockPermissions (User/Domain) "
            + "table has no Condition or Component column to honor feature gating.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Permissions,
            static (p, i) => p.FeatureRef is not null && string.IsNullOrEmpty(p.Sddl)
                ? new Violation(new RuleId("PRM005"), Severity.Error,
                    ModelPath.Root.Field("Permissions").Index(i),
                    $"Permission for '{p.LockObject}' declares FeatureRef '{p.FeatureRef}' but has no SDDL. " +
                    "MSI's LockPermissions (User/Domain) table has no Condition or Component column, so " +
                    "only SDDL-driven permissions (MsiLockPermissionsEx) can be feature-gated.")
                : null));

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
}
