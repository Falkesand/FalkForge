using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for <see cref="PackageModel"/> top-level metadata (PKG001-011).
/// Each rule is a <c>public static readonly</c> field so tests can invoke it directly
/// without going through the full orchestrator.
/// </summary>
public static class PackageRules
{
    private static readonly ModelPath NamePath = ModelPath.Root.Field("Name");
    private static readonly ModelPath ManufacturerPath = ModelPath.Root.Field("Manufacturer");
    private static readonly ModelPath VersionPath = ModelPath.Root.Field("Version");
    private static readonly ModelPath UpgradeCodePath = ModelPath.Root.Field("UpgradeCode");
    private static readonly ModelPath ProductCodePath = ModelPath.Root.Field("ProductCode");

    /// <summary>PKG001 — Package Name is required.</summary>
    public static readonly ValidationRule Pkg001_NameRequired = ValidationRule.Single(
        new RuleId("PKG001"),
        Severity.Error,
        ModelSection.Package,
        "Name required",
        "Package Name must not be null, empty, or whitespace-only.",
        static ctx => string.IsNullOrWhiteSpace(ctx.Package.Name)
            ? new Violation(new RuleId("PKG001"), Severity.Error, NamePath, "Package Name is required.")
            : null);

    /// <summary>PKG002 — Package Manufacturer is required.</summary>
    public static readonly ValidationRule Pkg002_ManufacturerRequired = ValidationRule.Single(
        new RuleId("PKG002"),
        Severity.Error,
        ModelSection.Package,
        "Manufacturer required",
        "Package Manufacturer must not be null, empty, or whitespace-only.",
        static ctx => string.IsNullOrWhiteSpace(ctx.Package.Manufacturer)
            ? new Violation(new RuleId("PKG002"), Severity.Error, ManufacturerPath, "Package Manufacturer is required.")
            : null);

    /// <summary>PKG003 — Package Version is required.</summary>
    public static readonly ValidationRule Pkg003_VersionRequired = ValidationRule.Single(
        new RuleId("PKG003"),
        Severity.Error,
        ModelSection.Package,
        "Version required",
        "Package Version must not be null.",
        static ctx => ctx.Package.Version is null
            ? new Violation(new RuleId("PKG003"), Severity.Error, VersionPath, "Package Version is required.")
            : null);

    /// <summary>PKG004 — Version 0.0.0 warning.</summary>
    public static readonly ValidationRule Pkg004_VersionZeroWarning = ValidationRule.Single(
        new RuleId("PKG004"),
        Severity.Warning,
        ModelSection.Package,
        "Version is 0.0.0",
        "Package Version 0.0.0 may indicate an unset version.",
        static ctx =>
        {
            var v = ctx.Package.Version;
            return v is not null && v.Major == 0 && v.Minor == 0 && v.Build == 0
                ? new Violation(new RuleId("PKG004"), Severity.Warning, VersionPath, "Package Version is 0.0.0.")
                : null;
        });

    /// <summary>PKG005 — MSI major version cannot exceed 255.</summary>
    public static readonly ValidationRule Pkg005_MajorVersionLimit = ValidationRule.Single(
        new RuleId("PKG005"),
        Severity.Error,
        ModelSection.Package,
        "Major version exceeds 255",
        "MSI major version field is one byte; values above 255 overflow.",
        static ctx => ctx.Package.Version is not null && ctx.Package.Version.Major > 255
            ? new Violation(new RuleId("PKG005"), Severity.Error, VersionPath, "MSI major version cannot exceed 255.")
            : null);

    /// <summary>PKG006 — MSI minor version cannot exceed 255.</summary>
    public static readonly ValidationRule Pkg006_MinorVersionLimit = ValidationRule.Single(
        new RuleId("PKG006"),
        Severity.Error,
        ModelSection.Package,
        "Minor version exceeds 255",
        "MSI minor version field is one byte; values above 255 overflow.",
        static ctx => ctx.Package.Version is not null && ctx.Package.Version.Minor > 255
            ? new Violation(new RuleId("PKG006"), Severity.Error, VersionPath, "MSI minor version cannot exceed 255.")
            : null);

    /// <summary>PKG007 — MSI build version cannot exceed 65535.</summary>
    public static readonly ValidationRule Pkg007_BuildVersionLimit = ValidationRule.Single(
        new RuleId("PKG007"),
        Severity.Error,
        ModelSection.Package,
        "Build version exceeds 65535",
        "MSI build version field is two bytes; values above 65535 overflow.",
        static ctx => ctx.Package.Version is not null && ctx.Package.Version.Build > 65535
            ? new Violation(new RuleId("PKG007"), Severity.Error, VersionPath, "MSI build version cannot exceed 65535.")
            : null);

    /// <summary>PKG008 — Package Name longer than 64 characters warning.</summary>
    public static readonly ValidationRule Pkg008_NameLengthWarning = ValidationRule.Single(
        new RuleId("PKG008"),
        Severity.Warning,
        ModelSection.Package,
        "Name exceeds 64 characters",
        "Names longer than 64 characters may cause display issues in some installer UI.",
        static ctx => ctx.Package.Name?.Length > 64
            ? new Violation(new RuleId("PKG008"), Severity.Warning, NamePath,
                "Package Name exceeds 64 characters, which may cause display issues.")
            : null);

    /// <summary>PKG009 — UpgradeCode must not be empty GUID.</summary>
    public static readonly ValidationRule Pkg009_UpgradeCodeRequired = ValidationRule.Single(
        new RuleId("PKG009"),
        Severity.Error,
        ModelSection.Package,
        "UpgradeCode must not be empty GUID",
        "An empty UpgradeCode prevents the Windows Installer upgrade table from working correctly.",
        static ctx => ctx.Package.UpgradeCode == Guid.Empty
            ? new Violation(new RuleId("PKG009"), Severity.Error, UpgradeCodePath, "UpgradeCode must not be empty GUID.")
            : null);

    /// <summary>PKG010 — ProductCode must not be empty GUID.</summary>
    public static readonly ValidationRule Pkg010_ProductCodeRequired = ValidationRule.Single(
        new RuleId("PKG010"),
        Severity.Error,
        ModelSection.Package,
        "ProductCode must not be empty GUID",
        "An empty ProductCode is not a valid MSI product identifier.",
        static ctx => ctx.Package.ProductCode == Guid.Empty
            ? new Violation(new RuleId("PKG010"), Severity.Error, ProductCodePath, "ProductCode must not be empty GUID.")
            : null);

    /// <summary>PKG011 — Package has no files warning.</summary>
    public static readonly ValidationRule Pkg011_EmptyPackageWarning = ValidationRule.Single(
        new RuleId("PKG011"),
        Severity.Warning,
        ModelSection.Package,
        "Empty package",
        "A package with no files and no feature component refs produces an empty MSI.",
        static ctx =>
        {
            var p = ctx.Package;
            if (p.Files.Count > 0)
                return null;
            if (p.Features.Any(f => f.ComponentRefs.Count > 0))
                return null;
            return new Violation(new RuleId("PKG011"), Severity.Warning, ModelPath.Root,
                "Package has no files. The MSI will be empty.");
        });

    /// <summary>
    /// All PKG rules in order, ready to be included in a <see cref="RuleRegistry"/>.
    /// </summary>
    public static readonly ValidationRule[] All =
    [
        Pkg001_NameRequired,
        Pkg002_ManufacturerRequired,
        Pkg003_VersionRequired,
        Pkg004_VersionZeroWarning,
        Pkg005_MajorVersionLimit,
        Pkg006_MinorVersionLimit,
        Pkg007_BuildVersionLimit,
        Pkg008_NameLengthWarning,
        Pkg009_UpgradeCodeRequired,
        Pkg010_ProductCodeRequired,
        Pkg011_EmptyPackageWarning
    ];
}
