using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class MiscRules
{
    // ── Shortcuts ────────────────────────────────────────────────────────────

    /// <summary>SHC001 — Shortcut Name is required.</summary>
    public static readonly ValidationRule Shc001_NameRequired = new(
        new RuleId("SHC001"),
        Severity.Error,
        ModelSection.Shortcut,
        "Shortcut Name required",
        "Every shortcut must have a non-empty Name.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Shortcuts,
            static (s, i) => string.IsNullOrWhiteSpace(s.Name)
                ? new Violation(new RuleId("SHC001"), Severity.Error,
                    ModelPath.Root.Field("Shortcuts").Index(i).Field("Name"),
                    "Shortcut Name is required.")
                : null));

    /// <summary>SHC002 — Shortcut TargetFile is required.</summary>
    public static readonly ValidationRule Shc002_TargetFileRequired = new(
        new RuleId("SHC002"),
        Severity.Error,
        ModelSection.Shortcut,
        "Shortcut TargetFile required",
        "Every shortcut must reference a non-empty TargetFile.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Shortcuts,
            static (s, i) => string.IsNullOrWhiteSpace(s.TargetFile)
                ? new Violation(new RuleId("SHC002"), Severity.Error,
                    ModelPath.Root.Field("Shortcuts").Index(i).Field("TargetFile"),
                    $"Shortcut '{s.Name}' must have a TargetFile.")
                : null));

    /// <summary>SHC003 — Shortcut has no locations (warning).</summary>
    public static readonly ValidationRule Shc003_LocationsWarning = new(
        new RuleId("SHC003"),
        Severity.Warning,
        ModelSection.Shortcut,
        "Shortcut has no locations",
        "A shortcut with no locations will not appear on the start menu or desktop.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.Shortcuts,
            static (s, i) => s.Locations.Count == 0
                ? new Violation(new RuleId("SHC003"), Severity.Warning,
                    ModelPath.Root.Field("Shortcuts").Index(i).Field("Locations"),
                    $"Shortcut '{s.Name}' has no locations specified.")
                : null));

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

    // ── File associations ─────────────────────────────────────────────────────

    /// <summary>FAS001 — File association Extension is required.</summary>
    public static readonly ValidationRule Fas001_ExtensionRequired = new(
        new RuleId("FAS001"),
        Severity.Error,
        ModelSection.Package,
        "File association Extension required",
        "Every file association must specify a file extension.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.FileAssociations,
            static (a, i) => string.IsNullOrWhiteSpace(a.Extension)
                ? new Violation(new RuleId("FAS001"), Severity.Error,
                    ModelPath.Root.Field("FileAssociations").Index(i).Field("Extension"),
                    "File association Extension is required.")
                : null));

    /// <summary>FAS002 — File association ProgId is required.</summary>
    public static readonly ValidationRule Fas002_ProgIdRequired = new(
        new RuleId("FAS002"),
        Severity.Error,
        ModelSection.Package,
        "File association ProgId required",
        "Every file association must specify a ProgId.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.FileAssociations,
            static (a, i) => string.IsNullOrWhiteSpace(a.ProgId)
                ? new Violation(new RuleId("FAS002"), Severity.Error,
                    ModelPath.Root.Field("FileAssociations").Index(i).Field("ProgId"),
                    $"File association '{a.Extension}' must have a ProgId.")
                : null));

    /// <summary>FAS003 — File association has no verbs (warning).</summary>
    public static readonly ValidationRule Fas003_VerbsWarning = new(
        new RuleId("FAS003"),
        Severity.Warning,
        ModelSection.Package,
        "File association has no verbs",
        "A file association without verbs will not create any shell commands for the extension.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.FileAssociations,
            static (a, i) => a.Verbs.Count == 0
                ? new Violation(new RuleId("FAS003"), Severity.Warning,
                    ModelPath.Root.Field("FileAssociations").Index(i).Field("Verbs"),
                    $"File association '{a.Extension}' has no verbs defined.")
                : null));
}
