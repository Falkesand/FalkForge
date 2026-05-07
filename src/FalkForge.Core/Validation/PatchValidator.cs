using System.Collections.Immutable;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Validation;

public static class PatchValidator
{
    // ── Rule metadata (RFC cycle-3) ──────────────────────────────────────────

    /// <summary>MSP001 — Patch TargetMsiPath is required.</summary>
    public static readonly ValidationRule Msp001_TargetMsiPathRequired = new(
        new RuleId("MSP001"),
        Severity.Error,
        ModelSection.Patch,
        "Patch TargetMsiPath required",
        "TargetMsiPath must point to the baseline MSI file being patched.",
        static _ => []);

    /// <summary>MSP002 — Patch UpdatedMsiPath is required.</summary>
    public static readonly ValidationRule Msp002_UpdatedMsiPathRequired = new(
        new RuleId("MSP002"),
        Severity.Error,
        ModelSection.Patch,
        "Patch UpdatedMsiPath required",
        "UpdatedMsiPath must point to the updated MSI file from which deltas are computed.",
        static _ => []);

    /// <summary>MSP003 — Patch Classification must be a defined enum value.</summary>
    public static readonly ValidationRule Msp003_ClassificationRequired = new(
        new RuleId("MSP003"),
        Severity.Error,
        ModelSection.Patch,
        "Patch Classification required",
        "Classification must be set to a valid PatchClassification value.",
        static _ => []);

    /// <summary>MSP004 — Patch Id must not be empty GUID.</summary>
    public static readonly ValidationRule Msp004_IdRequired = new(
        new RuleId("MSP004"),
        Severity.Error,
        ModelSection.Patch,
        "Patch Id required",
        "The patch must have a non-empty GUID identifier.",
        static _ => []);

    /// <summary>All MSP rule metadata in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Msp001_TargetMsiPathRequired,
        Msp002_UpdatedMsiPathRequired,
        Msp003_ClassificationRequired,
        Msp004_IdRequired
    ];

    // ── Engine-based API (new) ───────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation happy-path check. Returns <see cref="Result{Unit}.Success"/> on a
    /// clean model; aggregates all error messages into a single <see cref="Result{Unit}.Failure"/>.
    /// </summary>
    public static Result<Unit> Check(PatchModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var report = Inspect(model);
        if (report.IsValid)
            return Result<Unit>.Success(Unit.Value);

        var sb = new StringBuilder("Patch validation failed: ");
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
    public static ValidationReport Inspect(PatchModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var violations = ImmutableArray.CreateBuilder<Violation>(4);

        if (string.IsNullOrWhiteSpace(model.TargetMsiPath))
            violations.Add(new Violation(new RuleId("MSP001"), Severity.Error,
                ModelPath.Root.Field("TargetMsiPath"),
                "Patch TargetMsiPath is required."));

        if (string.IsNullOrWhiteSpace(model.UpdatedMsiPath))
            violations.Add(new Violation(new RuleId("MSP002"), Severity.Error,
                ModelPath.Root.Field("UpdatedMsiPath"),
                "Patch UpdatedMsiPath is required."));

        if (!Enum.IsDefined(model.Classification))
            violations.Add(new Violation(new RuleId("MSP003"), Severity.Error,
                ModelPath.Root.Field("Classification"),
                "Patch Classification is required and must be a valid value."));

        if (model.Id == Guid.Empty)
            violations.Add(new Violation(new RuleId("MSP004"), Severity.Error,
                ModelPath.Root.Field("Id"),
                "Patch Id is required and must be a valid GUID."));

        return violations.Count == 0
            ? ValidationReport.Empty
            : new ValidationReport(violations.ToImmutable());
    }

    // ── Legacy API — kept while callers migrate ──────────────────────────────

    public static ValidationResult Validate(PatchModel model)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(model.TargetMsiPath))
            result.AddError("MSP001", "Patch TargetMsiPath is required.");

        if (string.IsNullOrWhiteSpace(model.UpdatedMsiPath))
            result.AddError("MSP002", "Patch UpdatedMsiPath is required.");

        if (!Enum.IsDefined(model.Classification))
            result.AddError("MSP003", "Patch Classification is required and must be a valid value.");

        if (model.Id == Guid.Empty)
            result.AddError("MSP004", "Patch Id is required and must be a valid GUID.");

        return result;
    }
}
