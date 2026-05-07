using System.Collections.Immutable;
using System.Text;

namespace FalkForge.Validation;

/// <summary>
/// The output of a validation run — an immutable snapshot of all violations found.
/// </summary>
public sealed record ValidationReport(ImmutableArray<Violation> Violations)
{
    /// <summary>True when no violations have <see cref="Severity.Error"/> severity.</summary>
    public bool IsValid => !Violations.Any(v => v.Severity == Severity.Error);

    /// <summary>All violations with <see cref="Severity.Error"/> severity.</summary>
    public IEnumerable<Violation> Errors => Violations.Where(v => v.Severity == Severity.Error);

    /// <summary>All violations with <see cref="Severity.Warning"/> severity.</summary>
    public IEnumerable<Violation> Warnings => Violations.Where(v => v.Severity == Severity.Warning);

    /// <summary>All violations with <see cref="Severity.Info"/> severity.</summary>
    public IEnumerable<Violation> Infos => Violations.Where(v => v.Severity == Severity.Info);

    /// <summary>
    /// Converts to a <see cref="Result{Unit}"/>. On failure aggregates all error messages.
    /// This is the single authoritative formatter so <c>Check</c> and <c>Inspect</c>
    /// produce identical error strings.
    /// </summary>
    public Result<Unit> ToResult()
    {
        if (IsValid)
            return Result<Unit>.Success(Unit.Value);

        // StringBuilder avoids string concatenation in failure path.
        var sb = new StringBuilder("Package validation failed: ");
        var first = true;
        foreach (var v in Errors)
        {
            if (!first) sb.Append("; ");
            sb.Append(v.RuleId.Value);
            sb.Append(": ");
            sb.Append(v.Message);
            first = false;
        }

        return Result<Unit>.Failure(ErrorKind.Validation, sb.ToString());
    }

    /// <summary>Groups violations by rule ID for programmatic lookup.</summary>
    public ILookup<string, Violation> ByRule()
        => Violations.ToLookup(v => v.RuleId.Value, StringComparer.Ordinal);

    /// <summary>Empty report — no violations found.</summary>
    public static readonly ValidationReport Empty = new(ImmutableArray<Violation>.Empty);
}
