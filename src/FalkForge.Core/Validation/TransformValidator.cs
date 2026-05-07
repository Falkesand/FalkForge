using System.Collections.Immutable;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Validation;

public static class TransformValidator
{
    // ── Rule metadata (RFC cycle-3) ──────────────────────────────────────────

    /// <summary>MST001 — Transform BaseMsiPath is required.</summary>
    public static readonly ValidationRule Mst001_BaseMsiPathRequired = new(
        new RuleId("MST001"),
        Severity.Error,
        ModelSection.Transform,
        "Transform BaseMsiPath required",
        "BaseMsiPath must point to the baseline MSI from which the transform is computed.",
        static _ => []);

    /// <summary>MST002 — Transform TargetMsiPath is required.</summary>
    public static readonly ValidationRule Mst002_TargetMsiPathRequired = new(
        new RuleId("MST002"),
        Severity.Error,
        ModelSection.Transform,
        "Transform TargetMsiPath required",
        "TargetMsiPath must point to the updated MSI that the transform will produce.",
        static _ => []);

    /// <summary>All MST rule metadata in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Mst001_BaseMsiPathRequired,
        Mst002_TargetMsiPathRequired
    ];

    // ── Engine-based API (new) ───────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation happy-path check. Returns <see cref="Result{Unit}.Success"/> on a
    /// clean model; aggregates all error messages into a single <see cref="Result{Unit}.Failure"/>.
    /// </summary>
    public static Result<Unit> Check(TransformModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var report = Inspect(model);
        if (report.IsValid)
            return Result<Unit>.Success(Unit.Value);

        var sb = new StringBuilder("Transform validation failed: ");
        var first = true;
        foreach (var v in report.Errors)
        {
            if (!first) sb.Append("; ");
            sb.Append(v.RuleId.Value);
            sb.Append(": ");
            sb.Append(v.Message);
            first = false;
        }
        return Result<Unit>.Failure(ErrorKind.Validation, sb.ToString());
    }

    /// <summary>
    /// Rich validation report with structured locations and rule metadata.
    /// </summary>
    public static ValidationReport Inspect(TransformModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var violations = ImmutableArray.CreateBuilder<Violation>(2);

        if (string.IsNullOrWhiteSpace(model.BaseMsiPath))
            violations.Add(new Violation(new RuleId("MST001"), Severity.Error,
                ModelPath.Root.Field("BaseMsiPath"),
                "Transform BaseMsiPath is required."));

        if (string.IsNullOrWhiteSpace(model.TargetMsiPath))
            violations.Add(new Violation(new RuleId("MST002"), Severity.Error,
                ModelPath.Root.Field("TargetMsiPath"),
                "Transform TargetMsiPath is required."));

        return violations.Count == 0
            ? ValidationReport.Empty
            : new ValidationReport(violations.ToImmutable());
    }

}
