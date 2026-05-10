using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class MiscRules
{
    // ── Remove files ──────────────────────────────────────────────────────────

    /// <summary>RMF001 — RemoveFile DirectoryRef is required.</summary>
    public static readonly ValidationRule Rmf001_DirectoryRefRequired = new(
        new RuleId("RMF001"),
        Severity.Error,
        ModelSection.Package,
        "RemoveFile DirectoryRef required",
        "Every RemoveFile entry must specify a DirectoryRef.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RemoveFiles,
            static (rf, i) => string.IsNullOrWhiteSpace(rf.DirectoryRef)
                ? new Violation(new RuleId("RMF001"), Severity.Error,
                    ModelPath.Root.Field("RemoveFiles").Index(i).Field("DirectoryRef"),
                    $"RemoveFile '{rf.Id}' must have a DirectoryRef.")
                : null));

    /// <summary>RMF002 — RemoveFile must specify at least OnInstall or OnUninstall.</summary>
    public static readonly ValidationRule Rmf002_InstallOrUninstallRequired = new(
        new RuleId("RMF002"),
        Severity.Error,
        ModelSection.Package,
        "RemoveFile install/uninstall trigger required",
        "A RemoveFile entry with neither OnInstall nor OnUninstall set will never execute.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RemoveFiles,
            static (rf, i) => !rf.OnInstall && !rf.OnUninstall
                ? new Violation(new RuleId("RMF002"), Severity.Error,
                    ModelPath.Root.Field("RemoveFiles").Index(i),
                    $"RemoveFile '{rf.Id}' must specify at least one of OnInstall or OnUninstall.")
                : null));

    // ── Create folders ────────────────────────────────────────────────────────

    /// <summary>CRF001 — CreateFolder DirectoryRef is required.</summary>
    public static readonly ValidationRule Crf001_DirectoryRefRequired = new(
        new RuleId("CRF001"),
        Severity.Error,
        ModelSection.Package,
        "CreateFolder DirectoryRef required",
        "Every CreateFolder entry must specify a DirectoryRef.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.CreateFolders,
            static (cf, i) => string.IsNullOrWhiteSpace(cf.DirectoryRef)
                ? new Violation(new RuleId("CRF001"), Severity.Error,
                    ModelPath.Root.Field("CreateFolders").Index(i).Field("DirectoryRef"),
                    $"CreateFolder '{cf.Id}' must have a DirectoryRef.")
                : null));

    // ── Move files ────────────────────────────────────────────────────────────

    /// <summary>MVF001 — MoveFile SourceDirectory is required.</summary>
    public static readonly ValidationRule Mvf001_SourceDirectoryRequired = new(
        new RuleId("MVF001"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile SourceDirectory required",
        "Every MoveFile entry must specify a SourceDirectory.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.MoveFiles,
            static (mf, i) => string.IsNullOrWhiteSpace(mf.SourceDirectory)
                ? new Violation(new RuleId("MVF001"), Severity.Error,
                    ModelPath.Root.Field("MoveFiles").Index(i).Field("SourceDirectory"),
                    $"MoveFile '{mf.Id}' must have a SourceDirectory.")
                : null));

    /// <summary>MVF002 — MoveFile SourceFileName is required.</summary>
    public static readonly ValidationRule Mvf002_SourceFileNameRequired = new(
        new RuleId("MVF002"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile SourceFileName required",
        "Every MoveFile entry must specify a SourceFileName.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.MoveFiles,
            static (mf, i) => string.IsNullOrWhiteSpace(mf.SourceFileName)
                ? new Violation(new RuleId("MVF002"), Severity.Error,
                    ModelPath.Root.Field("MoveFiles").Index(i).Field("SourceFileName"),
                    $"MoveFile '{mf.Id}' must have a SourceFileName.")
                : null));

    /// <summary>MVF003 — MoveFile DestDirectory is required.</summary>
    public static readonly ValidationRule Mvf003_DestDirectoryRequired = new(
        new RuleId("MVF003"),
        Severity.Error,
        ModelSection.Package,
        "MoveFile DestDirectory required",
        "Every MoveFile entry must specify a DestDirectory.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.MoveFiles,
            static (mf, i) => string.IsNullOrWhiteSpace(mf.DestDirectory)
                ? new Violation(new RuleId("MVF003"), Severity.Error,
                    ModelPath.Root.Field("MoveFiles").Index(i).Field("DestDirectory"),
                    $"MoveFile '{mf.Id}' must have a DestDirectory.")
                : null));

    // ── Duplicate files ───────────────────────────────────────────────────────

    /// <summary>DPF001 — DuplicateFile FileRef is required.</summary>
    public static readonly ValidationRule Dpf001_FileRefRequired = new(
        new RuleId("DPF001"),
        Severity.Error,
        ModelSection.Package,
        "DuplicateFile FileRef required",
        "Every DuplicateFile entry must specify the source FileRef.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.DuplicateFiles,
            static (df, i) => string.IsNullOrWhiteSpace(df.FileRef)
                ? new Violation(new RuleId("DPF001"), Severity.Error,
                    ModelPath.Root.Field("DuplicateFiles").Index(i).Field("FileRef"),
                    $"DuplicateFile '{df.Id}' must have a FileRef.")
                : null));
}
