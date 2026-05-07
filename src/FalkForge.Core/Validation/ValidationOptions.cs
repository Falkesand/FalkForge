using System.Collections.Frozen;

namespace FalkForge.Validation;

/// <summary>
/// Per-run configuration for the validation engine.
/// Immutable record — create a modified copy with <c>with</c> expressions.
/// </summary>
public sealed record ValidationOptions
{
    /// <summary>Rule IDs to suppress — matching violations are not included in the report.</summary>
    public IReadOnlySet<string> IgnoredRules { get; init; } = FrozenSet<string>.Empty;

    /// <summary>When true, all <see cref="Severity.Warning"/> violations are promoted to <see cref="Severity.Error"/>.</summary>
    public bool WarningsAsErrors { get; init; }

    /// <summary>When true, the engine stops after the first error violation.</summary>
    public bool StopOnFirstError { get; init; }

    /// <summary>
    /// Optional override registry. When null, the engine uses <see cref="CoreRuleCatalog.Package"/>
    /// (or the appropriate target catalog).
    /// </summary>
    public RuleRegistry? Rules { get; init; }

    /// <summary>Default options — no suppressions, warnings stay as warnings, no early exit.</summary>
    public static ValidationOptions Default { get; } = new();
}
