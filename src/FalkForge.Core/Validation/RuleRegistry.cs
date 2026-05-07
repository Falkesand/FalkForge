using System.Collections.Frozen;
using System.Collections.Immutable;

namespace FalkForge.Validation;

/// <summary>
/// Immutable collection of <see cref="ValidationRule"/> instances.
/// All filter operations return new registries; no mutation occurs.
/// Uses a <see cref="FrozenDictionary{TKey,TValue}"/> for O(1) lookup by ID.
/// </summary>
public sealed class RuleRegistry
{
    /// <summary>All rules in this registry, in registration order.</summary>
    public ImmutableArray<ValidationRule> Rules { get; }

    /// <summary>FrozenDictionary for O(1) rule lookup by <see cref="RuleId"/>.</summary>
    public FrozenDictionary<RuleId, ValidationRule> ById { get; }

    public RuleRegistry(ImmutableArray<ValidationRule> rules)
    {
        Rules = rules;
        ById = rules.ToFrozenDictionary(r => r.Id);
    }

    /// <summary>Finds a rule by ID, or returns null if not found.</summary>
    public ValidationRule? Find(RuleId id)
        => ById.GetValueOrDefault(id);

    /// <summary>
    /// Returns a new registry with the specified rules removed.
    /// Used to build Patch/MergeModule/Transform catalogs from the base Package catalog.
    /// </summary>
    public RuleRegistry Without(params RuleId[] ids)
    {
        var excluded = ids.ToFrozenSet();
        return new RuleRegistry(Rules.Where(r => !excluded.Contains(r.Id)).ToImmutableArray());
    }

    /// <summary>Returns a new registry with the given rules appended.</summary>
    public RuleRegistry WithAdded(params ValidationRule[] extra)
        => new(Rules.AddRange(extra));

    /// <summary>
    /// Returns a new registry where the specified rule has its severity overridden.
    /// Used for per-project severity customisation.
    /// </summary>
    public RuleRegistry OverrideSeverity(RuleId id, Severity severity)
    {
        var updated = Rules.Select(r => r.Id.Equals(id) ? r with { Severity = severity } : r).ToImmutableArray();
        return new RuleRegistry(updated);
    }

    /// <summary>Returns a new registry containing only rules for the specified section.</summary>
    public RuleRegistry FilterSection(ModelSection section)
        => new(Rules.Where(r => r.Section == section).ToImmutableArray());
}
