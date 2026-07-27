using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

public static partial class TransformValidator
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

    /// <summary>MST003 — a Transform PropertyChanges key must be a legal PUBLIC MSI property identifier.</summary>
    public static readonly ValidationRule Mst003_PropertyChangeNameMustBeValidIdentifier = new(
        new RuleId("MST003"),
        Severity.Error,
        ModelSection.Transform,
        "Transform property name must be a valid PUBLIC MSI identifier",
        "Each PropertyChanges key must match [A-Z_][A-Z0-9_.]* (ALL UPPERCASE) -- it is written " +
        "verbatim into the target MSI's Property table.",
        static _ => []);

    /// <summary>All MST rule metadata in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Mst001_BaseMsiPathRequired,
        Mst002_TargetMsiPathRequired,
        Mst003_PropertyChangeNameMustBeValidIdentifier
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

        foreach (var propertyName in model.PropertyChanges.Keys)
        {
            if (!PublicPropertyIdentifierPattern().IsMatch(propertyName))
                violations.Add(new Violation(new RuleId("MST003"), Severity.Error,
                    ModelPath.Root.Field("PropertyChanges").Key(propertyName),
                    $"Transform property name '{propertyName}' is not a valid PUBLIC MSI property " +
                    "identifier (must match [A-Z_][A-Z0-9_.]*, ALL UPPERCASE). It is written verbatim " +
                    "into the target MSI's Property table, so an illegal name would either be rejected " +
                    "by msi.dll or silently produce a private/lowercase property instead of the intended " +
                    "public one."));
        }

        return violations.Count == 0
            ? ValidationReport.Empty
            : new ValidationReport(violations.ToImmutable());
    }

    /// <summary>
    ///     Legal identifier grammar for a PUBLIC MSI <c>Property</c> name -- mirrors
    ///     <c>DotNetSearchValidator.MsiIdentifierPattern</c> and <c>PropertyNameValidator</c>'s
    ///     <c>^[A-Z_][A-Z0-9_.]*$</c> rule used elsewhere in this codebase for the same concept.
    /// </summary>
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$")]
    private static partial Regex PublicPropertyIdentifierPattern();
}
