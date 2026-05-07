using System.Collections.Immutable;

namespace FalkForge.Validation;

/// <summary>
/// Canonical built-in rule registries. Static readonly, built once at module load.
/// Each non-package catalog is a filter expression over the base package registry
/// plus target-specific rules.
/// Rules are populated incrementally as rule classes are ported from the legacy
/// ModelValidator in RFC cycle-3. Currently PKG001-011 are live; remaining rules
/// are appended as later slices land.
/// </summary>
public static class CoreRuleCatalog
{
    /// <summary>
    /// All rules that apply to a <see cref="FalkForge.Models.PackageModel"/>.
    /// Populated from per-area static rule classes (PackageRules, ServiceRules, etc.)
    /// as they are ported from the legacy ModelValidator.
    /// </summary>
    public static RuleRegistry Package { get; internal set; }
        = BuildPackage();

    public static RuleRegistry MergeModule { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    public static RuleRegistry Patch { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    public static RuleRegistry Transform { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    private static RuleRegistry BuildPackage()
    {
        // Single flat list — each rule class appends its slice.
        // Ordering within a slice follows the rule ID numeric sequence.
        var all = ImmutableArray.CreateBuilder<ValidationRule>();
        all.AddRange(PackageRules.All);
        // ServiceRules, FeatureRules, etc. appended in subsequent slices.
        return new RuleRegistry(all.ToImmutable());
    }
}
