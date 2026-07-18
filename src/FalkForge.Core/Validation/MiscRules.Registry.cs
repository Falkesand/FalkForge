using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class MiscRules
{
    // ── Registry ──────────────────────────────────────────────────────────────

    /// <summary>REG001 — Registry entry Key is required.</summary>
    public static readonly ValidationRule Reg001_KeyRequired = new(
        new RuleId("REG001"),
        Severity.Error,
        ModelSection.Registry,
        "Registry entry Key required",
        "Every registry entry must specify a non-empty Key.",
        static ctx => ValidationCollectionHelper.ValidateCollection(ctx.Package.RegistryEntries,
            static (entry, i) => string.IsNullOrWhiteSpace(entry.Key)
                ? new Violation(new RuleId("REG001"), Severity.Error,
                    ModelPath.Root.Field("RegistryEntries").Index(i).Field("Key"),
                    "Registry entry Key is required.")
                : null));

    /// <summary>
    /// Identifies a registry entry by write location (root + key + value name). Key and value
    /// names are matched case-insensitively per Win32 registry semantics via
    /// <see cref="RegistryIdentityComparer"/> rather than pre-uppercasing each string (avoids an
    /// allocation per entry — Gate 6). The data payload itself is compared separately and is
    /// case-sensitive; see <see cref="RegistryValuesEqual"/>.
    /// </summary>
    private readonly record struct RegistryIdentity(RegistryRoot Root, string Key, string? ValueName);

    /// <summary>Case-insensitive (Win32 registry semantics) equality/hashing for <see cref="RegistryIdentity"/>.</summary>
    private sealed class RegistryIdentityComparer : IEqualityComparer<RegistryIdentity>
    {
        public static readonly RegistryIdentityComparer Instance = new();

        public bool Equals(RegistryIdentity x, RegistryIdentity y) =>
            x.Root == y.Root
            && string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ValueName, y.ValueName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(RegistryIdentity obj) =>
            HashCode.Combine(
                obj.Root,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key),
                obj.ValueName is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ValueName));
    }

    /// <summary>
    /// The effective MSI Component_ placement a registry entry resolves to, mirroring
    /// <c>RegistryTableProducer.ResolveComponentId</c>: an explicit
    /// <see cref="RegistryEntryModel.ComponentId"/> always wins (scope = that id, FeatureRef
    /// dimension zeroed out); otherwise a non-null <see cref="RegistryEntryModel.FeatureRef"/>
    /// routes the entry to its own synthesized, feature-gated component (scope = that feature
    /// id); with neither set, the entry falls onto the single shared default component (scope =
    /// both fields null). Two entries only share a scope -- and are therefore certain to
    /// co-install -- when their resolved scope is equal; anything else is only a *possible*
    /// conflict (e.g. two mutually-exclusive features), matching how MSI's own ICE30 treats
    /// cross-component collisions at the same registry location as a warning, not an error.
    /// </summary>
    private readonly record struct RegistryScope(string? ComponentId, string? FeatureRef)
    {
        public static RegistryScope From(RegistryEntryModel entry) =>
            entry.ComponentId is not null
                ? new RegistryScope(entry.ComponentId, null)
                : new RegistryScope(null, entry.FeatureRef);
    }

    /// <summary>
    /// Single left-to-right pass pairing every registry entry (after the first) with the
    /// earliest entry sharing its write location, plus whether their data is identical and
    /// whether they resolve to the same effective placement scope. Shared by REG002 (identical
    /// duplicate → warning) and REG003 (conflicting duplicate → error, downgraded to warning
    /// when the scopes differ) so the scan/group logic exists exactly once.
    /// </summary>
    private static IEnumerable<(int Index, int FirstIndex, bool SameValue, bool SameScope)> FindRegistryDuplicates(
        IReadOnlyList<RegistryEntryModel> entries)
    {
        var seen = new Dictionary<RegistryIdentity, int>(RegistryIdentityComparer.Instance);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue; // REG001 catches this

            var identity = new RegistryIdentity(entry.Root, entry.Key, entry.ValueName);

            if (!seen.TryGetValue(identity, out var firstIndex))
            {
                seen[identity] = i;
                continue;
            }

            var first = entries[firstIndex];
            yield return (
                i,
                firstIndex,
                RegistryValuesEqual(first, entry),
                RegistryScope.From(first) == RegistryScope.From(entry));
        }
    }

    /// <summary>
    /// Compares the data payload of two registry entries that share the same write location.
    /// Different <see cref="RegistryValueType"/> always conflicts; same type compares the
    /// stored value by its concrete representation (string, DWord, binary, or multi-string).
    /// </summary>
    private static bool RegistryValuesEqual(RegistryEntryModel a, RegistryEntryModel b)
    {
        if (a.ValueType != b.ValueType)
            return false;

        return (a.Value, b.Value) switch
        {
            (null, null) => true,
            (string sa, string sb) => string.Equals(sa, sb, StringComparison.Ordinal),
            (byte[] ba, byte[] bb) => ba.AsSpan().SequenceEqual(bb),
            (string[] ma, string[] mb) => ma.AsSpan().SequenceEqual(mb),
            (int ia, int ib) => ia == ib,
            _ => Equals(a.Value, b.Value)
        };
    }

    /// <summary>
    /// REG002 — Two registry entries write the same root+key+value-name with identical data
    /// (warning, redundant). Always a warning regardless of scope; the message notes possible
    /// feature-exclusivity when the two entries resolve to a different placement scope (see
    /// <see cref="RegistryScope"/>) since the redundancy may be intentional per-feature authoring.
    /// </summary>
    public static readonly ValidationRule Reg002_DuplicateEntry = new(
        new RuleId("REG002"),
        Severity.Warning,
        ModelSection.Registry,
        "Duplicate registry entry",
        "Two registry entries write the exact same root, key, and value name with identical data. The duplicate is redundant.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var (index, firstIndex, sameValue, sameScope) in FindRegistryDuplicates(ctx.Package.RegistryEntries))
            {
                if (!sameValue)
                    continue;

                var entry = ctx.Package.RegistryEntries[index];
                var location = $"{entry.Key}\\{entry.ValueName ?? "(Default)"}";
                var message = sameScope
                    ? $"Registry entry '{location}' duplicates the entry at index {firstIndex} with identical data."
                    : $"Registry entry '{location}' duplicates the entry at index {firstIndex} with identical data, " +
                      "but the two entries are gated to different components/features -- if they are mutually " +
                      "exclusive, this redundancy is harmless.";
                violations.Add(new Violation(new RuleId("REG002"), Severity.Warning,
                    ModelPath.Root.Field("RegistryEntries").Index(index), message));
            }
            return violations.ToImmutable();
        });

    /// <summary>
    /// REG003 — Two registry entries write the same root+key+value-name with conflicting data.
    /// Error when both entries resolve to the same placement scope (see <see cref="RegistryScope"/>)
    /// -- they are certain to co-install, so the value that lands in the registry depends on
    /// install order. Downgraded to warning when the scopes differ (e.g. two different
    /// FeatureRefs): nothing statically proves the features are mutually exclusive, so this is
    /// only a *possible* conflict -- matching how MSI's own ICE30 treats cross-component
    /// collisions at the same registry location as a warning, not a hard error.
    /// </summary>
    public static readonly ValidationRule Reg003_ConflictingEntry = new(
        new RuleId("REG003"),
        Severity.Error,
        ModelSection.Registry,
        "Conflicting registry entry",
        "Two registry entries write the same root, key, and value name with different data. The value that ends up in the registry depends on install order.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var (index, firstIndex, sameValue, sameScope) in FindRegistryDuplicates(ctx.Package.RegistryEntries))
            {
                if (sameValue)
                    continue;

                var entry = ctx.Package.RegistryEntries[index];
                var location = $"{entry.Key}\\{entry.ValueName ?? "(Default)"}";
                var severity = sameScope ? Severity.Error : Severity.Warning;
                var message = sameScope
                    ? $"Registry entry '{location}' conflicts with the entry at index {firstIndex}: same location, different data."
                    : $"Registry entry '{location}' conflicts with the entry at index {firstIndex}: same location, " +
                      "different data, but the two entries are gated to different components/features. This is " +
                      "only a real conflict if both can ever be installed together -- if the components/features " +
                      "are mutually exclusive, this is safe. Windows Installer itself treats cross-component " +
                      "collisions at the same registry location as a warning (ICE30), not a hard error.";
                violations.Add(new Violation(new RuleId("REG003"), severity,
                    ModelPath.Root.Field("RegistryEntries").Index(index), message));
            }
            return violations.ToImmutable();
        });

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
