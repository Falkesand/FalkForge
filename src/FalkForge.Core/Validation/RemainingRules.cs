using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for remaining model sections:
/// Custom actions (CA001-006), Assemblies (ASM001-003),
/// MediaTemplate (MDT001-004), Signing (SGN001-003),
/// MajorUpgrade (MUP001, MUP003), Downgrade (DNG001-002).
/// </summary>
public static partial class RemainingRules
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+\.\d+$")]
    private static partial Regex AssemblyVersionRegex();

    // ── Custom actions ────────────────────────────────────────────────────────

    /// <summary>CA001 — Custom action Id is required.</summary>
    public static readonly ValidationRule Ca001_IdRequired = new(
        new RuleId("CA001"),
        Severity.Error,
        ModelSection.CustomAction,
        "Custom action Id required",
        "Every custom action must have a non-empty Id.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                if (string.IsNullOrWhiteSpace(ca.Id))
                    violations.Add(new Violation(new RuleId("CA001"), Severity.Error,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("Id"),
                        "Custom action Id is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CA002 — Custom action Type must be non-zero.</summary>
    public static readonly ValidationRule Ca002_TypeRequired = new(
        new RuleId("CA002"),
        Severity.Error,
        ModelSection.CustomAction,
        "Custom action Type required",
        "A custom action with Type = 0 has no executable behavior.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                if (ca.Type == 0)
                    violations.Add(new Violation(new RuleId("CA002"), Severity.Error,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("Type"),
                        $"Custom action '{ca.Id}' must have a Type specified."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CA003 — Custom action SourceRef is required.</summary>
    public static readonly ValidationRule Ca003_SourceRefRequired = new(
        new RuleId("CA003"),
        Severity.Error,
        ModelSection.CustomAction,
        "Custom action SourceRef required",
        "Every custom action must reference its source via a non-empty SourceRef.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                if (string.IsNullOrWhiteSpace(ca.SourceRef))
                    violations.Add(new Violation(new RuleId("CA003"), Severity.Error,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("SourceRef"),
                        $"Custom action '{ca.Id}' must have a SourceRef."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CA004 — Rollback and Commit flags are mutually exclusive.</summary>
    public static readonly ValidationRule Ca004_RollbackCommitExclusive = new(
        new RuleId("CA004"),
        Severity.Error,
        ModelSection.CustomAction,
        "Rollback and Commit mutually exclusive",
        "A custom action cannot be scheduled as both Rollback and Commit.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                var hasRollback = (ca.Type & CustomActionType.Rollback) != 0;
                var hasCommit = (ca.Type & CustomActionType.Commit) != 0;
                if (hasRollback && hasCommit)
                    violations.Add(new Violation(new RuleId("CA004"), Severity.Error,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("Type"),
                        $"Custom action '{ca.Id}' cannot be both Rollback and Commit. These are mutually exclusive scheduling options."));
            }
            return violations.ToImmutable();
        });

    /// <summary>CA005 — NoImpersonate without InScript flag has no effect (warning).</summary>
    public static readonly ValidationRule Ca005_NoImpersonateRequiresInScript = new(
        new RuleId("CA005"),
        Severity.Warning,
        ModelSection.CustomAction,
        "NoImpersonate requires InScript",
        "NoImpersonate only applies to deferred/rollback/commit (in-script) actions.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                var hasNoImpersonate = (ca.Type & CustomActionType.NoImpersonate) != 0;
                var hasInScript = (ca.Type & CustomActionType.InScript) != 0;
                if (hasNoImpersonate && !hasInScript)
                    violations.Add(new Violation(new RuleId("CA005"), Severity.Warning,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("Type"),
                        $"Custom action '{ca.Id}' has NoImpersonate set but is not a deferred/rollback/commit action. NoImpersonate only applies to in-script actions."));
            }
            return violations.ToImmutable();
        });

    /// <summary>
    /// CA006 — Custom action defined but never scheduled (warning).
    /// <para>
    /// A custom action created via <c>PackageBuilder.CustomAction(id, ...)</c> only ever runs if
    /// Windows Installer is told when to run it — the classic WiX/MSI trap is authoring the
    /// action and forgetting to place it in a sequence, leaving an orphaned
    /// <c>CustomAction</c> table row that never executes.
    /// </para>
    /// <para>
    /// "Scheduled" here means the action's Id appears in <see cref="PackageModel.ExecuteSequenceActions"/>
    /// or <see cref="PackageModel.UISequenceActions"/> — the two lists <c>PackageBuilder.ExecuteSequence(...)</c>
    /// / <c>.UISequence(...)</c> populate, and the only source <c>InstallExecuteSequenceTableProducer</c> /
    /// <c>InstallUISequenceTableProducer</c> read when emitting sequence rows. The legacy
    /// <see cref="CustomActionModel.Sequence"/>/<see cref="CustomActionModel.After"/>/
    /// <see cref="CustomActionModel.Before"/> fields are intentionally NOT treated as scheduling —
    /// the current compiler never projects them onto a sequence table (see
    /// <c>CustomActionTableProducer</c>'s remarks), so a package that sets only those fields and
    /// skips <c>ExecuteSequence(...)</c>/<c>UISequence(...)</c> genuinely has an unscheduled action.
    /// </para>
    /// <para>
    /// This rule only sees what <see cref="PackageModel"/> carries, so it naturally avoids two
    /// false-positive sources without special-casing them: extension-contributed custom actions
    /// (Firewall, IIS, SQL, ...) are scheduled by a separate compiler-level execution-contributor
    /// pipeline and never populate <see cref="PackageModel.CustomActions"/> in the first place, and
    /// dialog <c>ControlEvent</c> rows (e.g. a checkbox's <c>DoAction</c>) are synthesized later
    /// during MSI dialog composition and are not part of the package model either. A type-51
    /// <c>SetProperty</c> action used purely to compute a value later tested by a condition is
    /// unremarkable here too — once it is scheduled via <c>ExecuteSequence</c>/<c>UISequence</c> it
    /// satisfies this rule like any other action; nothing else is required of it.
    /// </para>
    /// </summary>
    public static readonly ValidationRule Ca006_DefinedButNeverScheduled = new(
        new RuleId("CA006"),
        Severity.Warning,
        ModelSection.CustomAction,
        "Custom action never scheduled",
        "A custom action defined via CustomAction(...) but never referenced by ExecuteSequence(...) " +
        "or UISequence(...) never runs.",
        static ctx =>
        {
            if (ctx.Package.CustomActions.Count == 0)
                return [];

            var scheduled = new HashSet<string>(
                ctx.Package.ExecuteSequenceActions.Count + ctx.Package.UISequenceActions.Count,
                StringComparer.Ordinal);
            foreach (var action in ctx.Package.ExecuteSequenceActions)
                scheduled.Add(action.ActionName);
            foreach (var action in ctx.Package.UISequenceActions)
                scheduled.Add(action.ActionName);

            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomActions.Count; i++)
            {
                var ca = ctx.Package.CustomActions[i];
                if (!scheduled.Contains(ca.Id))
                    violations.Add(new Violation(new RuleId("CA006"), Severity.Warning,
                        ModelPath.Root.Field("CustomActions").Index(i).Field("Id"),
                        $"Custom action '{ca.Id}' is defined but never scheduled via ExecuteSequence(...) " +
                        "or UISequence(...) — Windows Installer will never run it. Reference its Id from " +
                        "PackageBuilder.ExecuteSequence(...)/.UISequence(...), or remove it if unused."));
            }
            return violations.ToImmutable();
        });

    // ── Assemblies ────────────────────────────────────────────────────────────

    /// <summary>ASM001 — Assembly FileRef is required.</summary>
    public static readonly ValidationRule Asm001_FileRefRequired = new(
        new RuleId("ASM001"),
        Severity.Error,
        ModelSection.Package,
        "Assembly FileRef required",
        "Every assembly entry must reference the file it is associated with.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Assemblies.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ctx.Package.Assemblies[i].FileRef))
                    violations.Add(new Violation(new RuleId("ASM001"), Severity.Error,
                        ModelPath.Root.Field("Assemblies").Index(i).Field("FileRef"),
                        "Assembly FileRef is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>ASM002 — GAC assembly should have a PublicKeyToken (warning).</summary>
    public static readonly ValidationRule Asm002_GacPublicKeyTokenWarning = new(
        new RuleId("ASM002"),
        Severity.Warning,
        ModelSection.Package,
        "GAC assembly should have PublicKeyToken",
        "Global Assembly Cache registration requires a strong name including a public key token.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Assemblies.Count; i++)
            {
                var a = ctx.Package.Assemblies[i];
                if (a.ApplicationFileRef is null && string.IsNullOrWhiteSpace(a.AssemblyPublicKeyToken))
                    violations.Add(new Violation(new RuleId("ASM002"), Severity.Warning,
                        ModelPath.Root.Field("Assemblies").Index(i).Field("AssemblyPublicKeyToken"),
                        $"GAC assembly '{a.FileRef}' should have a PublicKeyToken."));
            }
            return violations.ToImmutable();
        });

    /// <summary>ASM003 — Assembly version must match x.x.x.x format.</summary>
    public static readonly ValidationRule Asm003_VersionFormat = new(
        new RuleId("ASM003"),
        Severity.Error,
        ModelSection.Package,
        "Assembly version format invalid",
        "Assembly version must be in the format x.x.x.x (e.g. 1.2.3.4).",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.Assemblies.Count; i++)
            {
                var a = ctx.Package.Assemblies[i];
                if (!string.IsNullOrEmpty(a.AssemblyVersion) && !AssemblyVersionRegex().IsMatch(a.AssemblyVersion))
                    violations.Add(new Violation(new RuleId("ASM003"), Severity.Error,
                        ModelPath.Root.Field("Assemblies").Index(i).Field("AssemblyVersion"),
                        $"Assembly '{a.FileRef}' has invalid version format '{a.AssemblyVersion}'. Expected format: x.x.x.x."));
            }
            return violations.ToImmutable();
        });

    // ── MediaTemplate ─────────────────────────────────────────────────────────

    /// <summary>MDT001 — MediaTemplate CabinetTemplate is required.</summary>
    public static readonly ValidationRule Mdt001_CabinetTemplateRequired = ValidationRule.Single(
        new RuleId("MDT001"),
        Severity.Error,
        ModelSection.Package,
        "MediaTemplate CabinetTemplate required",
        "The CabinetTemplate must specify the naming pattern for cabinet files.",
        static ctx =>
        {
            if (ctx.Package.MediaTemplate is null) return null;
            return string.IsNullOrWhiteSpace(ctx.Package.MediaTemplate.CabinetTemplate)
                ? new Violation(new RuleId("MDT001"), Severity.Error,
                    ModelPath.Root.Field("MediaTemplate").Field("CabinetTemplate"),
                    "MediaTemplate CabinetTemplate is required.")
                : null;
        });

    /// <summary>MDT002 — CabinetTemplate must contain the {0} placeholder.</summary>
    public static readonly ValidationRule Mdt002_CabinetTemplatePlaceholder = ValidationRule.Single(
        new RuleId("MDT002"),
        Severity.Error,
        ModelSection.Package,
        "CabinetTemplate must contain {0}",
        "The {0} placeholder is required so the compiler can number multiple cabinets.",
        static ctx =>
        {
            var mt = ctx.Package.MediaTemplate;
            if (mt is null || string.IsNullOrWhiteSpace(mt.CabinetTemplate)) return null;
            return !mt.CabinetTemplate.Contains("{0}")
                ? new Violation(new RuleId("MDT002"), Severity.Error,
                    ModelPath.Root.Field("MediaTemplate").Field("CabinetTemplate"),
                    "MediaTemplate CabinetTemplate must contain '{0}' placeholder for cabinet numbering.")
                : null;
        });

    /// <summary>MDT003 — MaximumCabinetSizeInMB must not be negative.</summary>
    public static readonly ValidationRule Mdt003_CabinetSizeNonNegative = ValidationRule.Single(
        new RuleId("MDT003"),
        Severity.Error,
        ModelSection.Package,
        "MediaTemplate MaximumCabinetSizeInMB non-negative",
        "A negative cabinet size limit is not valid.",
        static ctx =>
        {
            var mt = ctx.Package.MediaTemplate;
            return mt is not null && mt.MaximumCabinetSizeInMB < 0
                ? new Violation(new RuleId("MDT003"), Severity.Error,
                    ModelPath.Root.Field("MediaTemplate").Field("MaximumCabinetSizeInMB"),
                    "MediaTemplate MaximumCabinetSizeInMB cannot be negative.")
                : null;
        });

    /// <summary>MDT004 — MaximumUncompressedMediaSize must not be negative.</summary>
    public static readonly ValidationRule Mdt004_UncompressedSizeNonNegative = ValidationRule.Single(
        new RuleId("MDT004"),
        Severity.Error,
        ModelSection.Package,
        "MediaTemplate MaximumUncompressedMediaSize non-negative",
        "A negative uncompressed media size limit is not valid.",
        static ctx =>
        {
            var mt = ctx.Package.MediaTemplate;
            return mt is not null && mt.MaximumUncompressedMediaSize < 0
                ? new Violation(new RuleId("MDT004"), Severity.Error,
                    ModelPath.Root.Field("MediaTemplate").Field("MaximumUncompressedMediaSize"),
                    "MediaTemplate MaximumUncompressedMediaSize cannot be negative.")
                : null;
        });

    // ── Signing ───────────────────────────────────────────────────────────────

    /// <summary>SGN001 — PFX certificate embeds private key (warning).</summary>
    public static readonly ValidationRule Sgn001_PfxCertificateWarning = ValidationRule.Single(
        new RuleId("SGN001"),
        Severity.Warning,
        ModelSection.Signing,
        "PFX certificate embeds private key",
        "PFX files contain the private key. Prefer certificate thumbprint from the certificate store.",
        static ctx =>
        {
            var s = ctx.Package.Signing;
            return s is not null
                   && !string.IsNullOrEmpty(s.CertificatePath)
                   && s.CertificatePath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
                ? new Violation(new RuleId("SGN001"), Severity.Warning,
                    ModelPath.Root.Field("Signing").Field("CertificatePath"),
                    "Using a PFX certificate file embeds the private key. Consider using a certificate thumbprint from the certificate store instead.")
                : null;
        });

    /// <summary>SGN002 — Signing requires CertificatePath or CertificateThumbprint.</summary>
    public static readonly ValidationRule Sgn002_CertificateRequired = ValidationRule.Single(
        new RuleId("SGN002"),
        Severity.Error,
        ModelSection.Signing,
        "Signing requires a certificate",
        "Signing must specify either CertificatePath (PFX file) or CertificateThumbprint (certificate store).",
        static ctx =>
        {
            var s = ctx.Package.Signing;
            return s is not null
                   && string.IsNullOrEmpty(s.CertificatePath)
                   && string.IsNullOrEmpty(s.CertificateThumbprint)
                ? new Violation(new RuleId("SGN002"), Severity.Error,
                    ModelPath.Root.Field("Signing"),
                    "Signing requires either CertificatePath or CertificateThumbprint.")
                : null;
        });

    /// <summary>SGN003 — DigestAlgorithm must be sha256, sha384, or sha512.</summary>
    public static readonly ValidationRule Sgn003_DigestAlgorithmValid = ValidationRule.Single(
        new RuleId("SGN003"),
        Severity.Error,
        ModelSection.Signing,
        "DigestAlgorithm must be sha256/sha384/sha512",
        "Only SHA-2 family digest algorithms are accepted for code signing.",
        static ctx =>
        {
            var s = ctx.Package.Signing;
            return s is not null
                   && !string.IsNullOrEmpty(s.DigestAlgorithm)
                   && s.DigestAlgorithm is not ("sha256" or "sha384" or "sha512")
                ? new Violation(new RuleId("SGN003"), Severity.Error,
                    ModelPath.Root.Field("Signing").Field("DigestAlgorithm"),
                    $"DigestAlgorithm '{s.DigestAlgorithm}' is not allowed. Must be one of: sha256, sha384, sha512.")
                : null;
        });

    // ── MajorUpgrade ──────────────────────────────────────────────────────────

    /// <summary>MUP001 — MajorUpgrade requires UpgradeCode.</summary>
    public static readonly ValidationRule Mup001_UpgradeCodeRequired = ValidationRule.Single(
        new RuleId("MUP001"),
        Severity.Error,
        ModelSection.MajorUpgrade,
        "MajorUpgrade requires UpgradeCode",
        "The Windows Installer upgrade table cannot function without an UpgradeCode.",
        static ctx => ctx.Package.MajorUpgrade is not null && ctx.Package.UpgradeCode == Guid.Empty
            ? new Violation(new RuleId("MUP001"), Severity.Error,
                ModelPath.Root.Field("UpgradeCode"),
                "MajorUpgrade requires UpgradeCode to be set on the package.")
            : null);

    /// <summary>MUP003 — MajorUpgrade and Upgrade table entries cannot coexist.</summary>
    public static readonly ValidationRule Mup003_NoConflictWithUpgradeTable = ValidationRule.Single(
        new RuleId("MUP003"),
        Severity.Error,
        ModelSection.MajorUpgrade,
        "MajorUpgrade conflicts with Upgrade table",
        "Use MajorUpgrade or Upgrade table entries — not both.",
        static ctx => ctx.Package.MajorUpgrade is not null && ctx.Package.Upgrade is not null
            ? new Violation(new RuleId("MUP003"), Severity.Error,
                ModelPath.Root.Field("MajorUpgrade"),
                "MajorUpgrade and Upgrade table entries cannot both be specified. Use one or the other.")
            : null);

    // ── Downgrade ─────────────────────────────────────────────────────────────

    /// <summary>DNG001 — Downgrade.Block() requires an error message.</summary>
    public static readonly ValidationRule Dng001_BlockRequiresMessage = ValidationRule.Single(
        new RuleId("DNG001"),
        Severity.Error,
        ModelSection.Package,
        "Downgrade block requires error message",
        "When blocking downgrades, an error message must be provided to inform the user.",
        static ctx =>
        {
            var d = ctx.Package.Downgrade;
            return d is not null && !d.AllowDowngrades && string.IsNullOrWhiteSpace(d.ErrorMessage)
                ? new Violation(new RuleId("DNG001"), Severity.Error,
                    ModelPath.Root.Field("Downgrade").Field("ErrorMessage"),
                    "Downgrade.Block() requires a non-empty error message.")
                : null;
        });

    /// <summary>DNG002 — Downgrade configuration requires MajorUpgrade.</summary>
    public static readonly ValidationRule Dng002_RequiresMajorUpgrade = ValidationRule.Single(
        new RuleId("DNG002"),
        Severity.Error,
        ModelSection.Package,
        "Downgrade requires MajorUpgrade",
        "Downgrade configuration has no effect without MajorUpgrade configured.",
        static ctx => ctx.Package.Downgrade is not null && ctx.Package.MajorUpgrade is null
            ? new Violation(new RuleId("DNG002"), Severity.Error,
                ModelPath.Root.Field("Downgrade"),
                "Downgrade configuration requires MajorUpgrade to be configured.")
            : null);

    /// <summary>All remaining rules in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Ca001_IdRequired,
        Ca002_TypeRequired,
        Ca003_SourceRefRequired,
        Ca004_RollbackCommitExclusive,
        Ca005_NoImpersonateRequiresInScript,
        Ca006_DefinedButNeverScheduled,
        Asm001_FileRefRequired,
        Asm002_GacPublicKeyTokenWarning,
        Asm003_VersionFormat,
        Mdt001_CabinetTemplateRequired,
        Mdt002_CabinetTemplatePlaceholder,
        Mdt003_CabinetSizeNonNegative,
        Mdt004_UncompressedSizeNonNegative,
        Sgn001_PfxCertificateWarning,
        Sgn002_CertificateRequired,
        Sgn003_DigestAlgorithmValid,
        Mup001_UpgradeCodeRequired,
        Mup003_NoConflictWithUpgradeTable,
        Dng001_BlockRequiresMessage,
        Dng002_RequiresMajorUpgrade
    ];
}
