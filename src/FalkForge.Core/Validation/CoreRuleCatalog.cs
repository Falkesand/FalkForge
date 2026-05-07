using System.Collections.Immutable;

namespace FalkForge.Validation;

/// <summary>
/// Canonical built-in rule registries. Static readonly, built once at module load.
/// Each non-package catalog is a filter expression over the base package registry
/// plus target-specific rules.
/// Rules are populated incrementally as rule classes (PackageRules, ServiceRules, etc.)
/// are added in RFC cycle-3.
/// </summary>
public static class CoreRuleCatalog
{
    // Populated by Slice 3+ — starts empty until rules are ported.
    public static RuleRegistry Package { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    public static RuleRegistry MergeModule { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    public static RuleRegistry Patch { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);

    public static RuleRegistry Transform { get; internal set; }
        = new(ImmutableArray<ValidationRule>.Empty);
}
