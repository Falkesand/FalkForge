namespace FalkForge.Validation;

/// <summary>
/// A single validation finding — the unit of output from a <see cref="ValidationRule"/>.
/// Immutable record. Created only when a rule detects a problem; never on the happy path.
/// </summary>
public sealed record Violation(
    RuleId RuleId,
    Severity Severity,
    ModelPath Path,
    string Message);
