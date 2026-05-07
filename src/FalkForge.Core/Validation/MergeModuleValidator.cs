using System.Collections.Immutable;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Validation;

public static class MergeModuleValidator
{
    // ── Rule metadata (RFC cycle-3) ──────────────────────────────────────────
    // Evaluate delegates are stubs — actual evaluation runs via Check/Inspect
    // which operate directly on MergeModuleModel without a RuleContext.

    /// <summary>MSM001 — Merge module Id must not be empty.</summary>
    public static readonly ValidationRule Msm001_IdRequired = new(
        new RuleId("MSM001"),
        Severity.Error,
        ModelSection.MergeModule,
        "Merge module Id required",
        "The merge module must have a non-empty GUID identifier.",
        static _ => []);   // evaluation via Check/Inspect

    /// <summary>MSM002 — Merge module Version must not be null.</summary>
    public static readonly ValidationRule Msm002_VersionRequired = new(
        new RuleId("MSM002"),
        Severity.Error,
        ModelSection.MergeModule,
        "Merge module Version required",
        "The merge module must have a non-null Version.",
        static _ => []);

    /// <summary>MSM003 — Merge module Language must not be zero.</summary>
    public static readonly ValidationRule Msm003_LanguageRequired = new(
        new RuleId("MSM003"),
        Severity.Error,
        ModelSection.MergeModule,
        "Merge module Language required",
        "Language code 0 is not a valid Windows Installer language identifier.",
        static _ => []);

    /// <summary>MSM004 — Merge module Manufacturer must not be empty.</summary>
    public static readonly ValidationRule Msm004_ManufacturerRequired = new(
        new RuleId("MSM004"),
        Severity.Error,
        ModelSection.MergeModule,
        "Merge module Manufacturer required",
        "Manufacturer is a required summary information property for merge modules.",
        static _ => []);

    /// <summary>All MSM rule metadata in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Msm001_IdRequired,
        Msm002_VersionRequired,
        Msm003_LanguageRequired,
        Msm004_ManufacturerRequired
    ];

    // ── Engine-based API (new) ───────────────────────────────────────────────

    /// <summary>
    /// Zero-allocation happy-path check. Returns <see cref="Result{Unit}.Success"/> on a
    /// clean model; aggregates all error messages into a single <see cref="Result{Unit}.Failure"/>.
    /// </summary>
    public static Result<Unit> Check(MergeModuleModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var report = Inspect(model);
        if (report.IsValid)
            return Result<Unit>.Success(Unit.Value);

        var sb = new StringBuilder("Merge module validation failed: ");
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
    public static ValidationReport Inspect(MergeModuleModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var violations = ImmutableArray.CreateBuilder<Violation>(4);

        if (model.Id == Guid.Empty)
            violations.Add(new Violation(new RuleId("MSM001"), Severity.Error,
                ModelPath.Root.Field("Id"),
                "Merge module Id is required and must be a valid GUID."));

        if (model.Version is null)
            violations.Add(new Violation(new RuleId("MSM002"), Severity.Error,
                ModelPath.Root.Field("Version"),
                "Merge module Version is required."));

        if (model.Language == 0)
            violations.Add(new Violation(new RuleId("MSM003"), Severity.Error,
                ModelPath.Root.Field("Language"),
                "Merge module Language is required (e.g. 1033 for English)."));

        if (string.IsNullOrWhiteSpace(model.Manufacturer))
            violations.Add(new Violation(new RuleId("MSM004"), Severity.Error,
                ModelPath.Root.Field("Manufacturer"),
                "Merge module Manufacturer is required."));

        return violations.Count == 0
            ? ValidationReport.Empty
            : new ValidationReport(violations.ToImmutable());
    }

    // ── Legacy API — kept while callers migrate ──────────────────────────────

    public static ValidationResult Validate(MergeModuleModel model)
    {
        var result = new ValidationResult();

        if (model.Id == Guid.Empty)
            result.AddError("MSM001", "Merge module Id is required and must be a valid GUID.");

        if (model.Version is null)
            result.AddError("MSM002", "Merge module Version is required.");

        if (model.Language == 0)
            result.AddError("MSM003", "Merge module Language is required (e.g. 1033 for English).");

        if (string.IsNullOrWhiteSpace(model.Manufacturer))
            result.AddError("MSM004", "Merge module Manufacturer is required.");

        return result;
    }
}
