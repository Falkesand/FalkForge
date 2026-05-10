using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class MiscRules
{
    // ── Registry ──────────────────────────────────────────────────────────────

    /// <summary>REG007 — Registry value references a sensitive MSI property (warning).</summary>
    public static readonly ValidationRule Reg007_SensitivePropertyInRegistry = new(
        new RuleId("REG007"),
        Severity.Warning,
        ModelSection.Registry,
        "Sensitive property in registry value",
        "Writing sensitive property values to the registry stores them in plaintext visible to any user or process with registry access.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RegistryEntries,
            static (entry, i) => entry.Value is string sv && ContainsSensitivePropertyReference(sv)
                ? new Violation(new RuleId("REG007"), Severity.Warning,
                    ModelPath.Root.Field("RegistryEntries").Index(i).Field("Value"),
                    $"Registry entry '{entry.Key}\\{entry.ValueName}' references a property that appears to contain sensitive data. " +
                    "Sensitive values written to the registry are stored in plaintext and visible to any user or process with registry access. " +
                    "Consider using a Windows service account or DPAPI-protected storage instead.")
                : null));

    // ── Remove registry ───────────────────────────────────────────────────────

    /// <summary>RRG001 — RemoveRegistry Id is required.</summary>
    public static readonly ValidationRule Rrg001_IdRequired = new(
        new RuleId("RRG001"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveRegistry Id required",
        "Every RemoveRegistry entry must have a non-empty Id.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RemoveRegistryEntries,
            static (e, i) => string.IsNullOrWhiteSpace(e.Id)
                ? new Violation(new RuleId("RRG001"), Severity.Error,
                    ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Id"),
                    "RemoveRegistry Id is required.")
                : null));

    /// <summary>RRG002 — RemoveRegistry Key is required.</summary>
    public static readonly ValidationRule Rrg002_KeyRequired = new(
        new RuleId("RRG002"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveRegistry Key required",
        "Every RemoveRegistry entry must specify the registry key to remove.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RemoveRegistryEntries,
            static (e, i) => string.IsNullOrWhiteSpace(e.Key)
                ? new Violation(new RuleId("RRG002"), Severity.Error,
                    ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Key"),
                    $"RemoveRegistry '{e.Id}' must have a Key.")
                : null));

    /// <summary>RRG003 — RemoveValue action requires a Name.</summary>
    public static readonly ValidationRule Rrg003_RemoveValueRequiresName = new(
        new RuleId("RRG003"),
        Severity.Error,
        ModelSection.Registry,
        "RemoveValue requires Name",
        "When using the RemoveValue action, a value Name must be specified.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RemoveRegistryEntries,
            static (e, i) => e.Action == RemoveRegistryAction.RemoveValue && string.IsNullOrWhiteSpace(e.Name)
                ? new Violation(new RuleId("RRG003"), Severity.Error,
                    ModelPath.Root.Field("RemoveRegistryEntries").Index(i).Field("Name"),
                    $"RemoveRegistry '{e.Id}' uses RemoveValue action but no Name specified.")
                : null));
}
