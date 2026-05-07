using System.Collections.Immutable;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for <see cref="FalkForge.Models.FeatureModel"/> (FEA001-005).
/// Uses <see cref="RuleContext.FeatureWalk"/> — the pre-flattened depth-first feature tree —
/// so no rule needs to recurse the tree independently.
/// </summary>
public static class FeatureRules
{
    /// <summary>FEA001 — Feature Id is required.</summary>
    public static readonly ValidationRule Fea001_IdRequired = new(
        new RuleId("FEA001"),
        Severity.Error,
        ModelSection.Feature,
        "Feature Id required",
        "Every feature must have a non-empty Id.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var entry in ctx.FeatureWalk)
            {
                if (string.IsNullOrWhiteSpace(entry.Feature.Id))
                    violations.Add(new Violation(
                        new RuleId("FEA001"),
                        Severity.Error,
                        entry.Path.Field("Id"),
                        "Feature Id is required."));
            }
            return violations.ToImmutable();
        });

    /// <summary>FEA002 — Feature Id must be unique across the feature tree.</summary>
    public static readonly ValidationRule Fea002_IdUnique = new(
        new RuleId("FEA002"),
        Severity.Error,
        ModelSection.Feature,
        "Feature Id unique",
        "Duplicate feature IDs cause undefined MSI behavior.",
        static ctx =>
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var entry in ctx.FeatureWalk)
            {
                var id = entry.Feature.Id;
                if (string.IsNullOrWhiteSpace(id))
                    continue; // FEA001 catches this
                if (!seen.Add(id))
                    violations.Add(new Violation(
                        new RuleId("FEA002"),
                        Severity.Error,
                        entry.Path.Field("Id"),
                        $"Duplicate feature Id: '{id}'."));
            }
            return violations.ToImmutable();
        });

    /// <summary>FEA003 — Feature Title is required.</summary>
    public static readonly ValidationRule Fea003_TitleRequired = new(
        new RuleId("FEA003"),
        Severity.Error,
        ModelSection.Feature,
        "Feature Title required",
        "Every feature must have a non-empty Title.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var entry in ctx.FeatureWalk)
            {
                if (string.IsNullOrWhiteSpace(entry.Feature.Title))
                    violations.Add(new Violation(
                        new RuleId("FEA003"),
                        Severity.Error,
                        entry.Path.Field("Title"),
                        $"Feature '{entry.Feature.Id}' must have a Title."));
            }
            return violations.ToImmutable();
        });

    /// <summary>FEA004 — Feature condition expression must not be empty.</summary>
    public static readonly ValidationRule Fea004_ConditionRequired = new(
        new RuleId("FEA004"),
        Severity.Error,
        ModelSection.Feature,
        "Feature condition expression required",
        "A feature condition with an empty condition string is invalid.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var entry in ctx.FeatureWalk)
            {
                for (var i = 0; i < entry.Feature.Conditions.Count; i++)
                {
                    var cond = entry.Feature.Conditions[i];
                    if (string.IsNullOrWhiteSpace(cond.Condition))
                        violations.Add(new Violation(
                            new RuleId("FEA004"),
                            Severity.Error,
                            entry.Path.Field("Conditions").Index(i).Field("Condition"),
                            $"Feature '{entry.Feature.Id}' has a condition with an empty condition string."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>FEA005 — Feature condition level must not be negative.</summary>
    public static readonly ValidationRule Fea005_ConditionLevelNonNegative = new(
        new RuleId("FEA005"),
        Severity.Warning,
        ModelSection.Feature,
        "Feature condition level non-negative",
        "A negative condition level has no defined meaning in the MSI feature table.",
        static ctx =>
        {
            var violations = ImmutableArray.CreateBuilder<Violation>();
            foreach (var entry in ctx.FeatureWalk)
            {
                for (var i = 0; i < entry.Feature.Conditions.Count; i++)
                {
                    var cond = entry.Feature.Conditions[i];
                    if (cond.Level < 0)
                        violations.Add(new Violation(
                            new RuleId("FEA005"),
                            Severity.Warning,
                            entry.Path.Field("Conditions").Index(i).Field("Level"),
                            $"Feature '{entry.Feature.Id}' has a condition with negative level {cond.Level}."));
                }
            }
            return violations.ToImmutable();
        });

    /// <summary>
    /// All FEA rules in order, ready to be included in a <see cref="RuleRegistry"/>.
    /// </summary>
    public static readonly ValidationRule[] All =
    [
        Fea001_IdRequired,
        Fea002_IdUnique,
        Fea003_TitleRequired,
        Fea004_ConditionRequired,
        Fea005_ConditionLevelNonNegative
    ];
}
