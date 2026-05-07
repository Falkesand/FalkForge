using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Immutable record describing a single validation rule.
/// Metadata fields carry ID, severity, section, title, description.
/// The Evaluate delegate closes over the model via a shared <see cref="RuleContext"/>.
/// Store rules as <c>public static readonly</c> fields in per-area classes
/// (e.g. <c>PackageRules</c>, <c>ServiceRules</c>) — one field per rule.
/// </summary>
public sealed record ValidationRule(
    RuleId Id,
    Severity Severity,
    ModelSection Section,
    string Title,
    string Description,
    Func<RuleContext, IEnumerable<Violation>> Evaluate)
{
    /// <summary>
    /// Convenience factory for the common "one scalar check" shape.
    /// The check delegate returns a single <see cref="Violation"/> or null.
    /// Wraps it into the full <see cref="Evaluate"/> signature.
    /// </summary>
    public static ValidationRule Single(
        RuleId id,
        Severity severity,
        ModelSection section,
        string title,
        string description,
        Func<RuleContext, Violation?> check)
        => new(id, severity, section, title, description,
            ctx =>
            {
                var v = check(ctx);
                return v is null ? [] : [v];
            });
}
