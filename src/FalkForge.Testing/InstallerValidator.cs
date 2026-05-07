using FalkForge.Models;
using FalkForge.Validation;

namespace FalkForge.Testing;

public static class InstallerValidator
{
    /// <summary>
    /// Runs full model validation and returns the structured report.
    /// Callers check <see cref="ValidationReport.IsValid"/> and iterate
    /// <see cref="ValidationReport.Errors"/> / <see cref="ValidationReport.Warnings"/>.
    /// </summary>
    public static ValidationReport Validate(PackageModel package)
    {
        return ModelValidator.Inspect(package);
    }

    /// <summary>
    /// Validates and throws <see cref="InvalidOperationException"/> if the package is invalid.
    /// Convenience method for test helpers that want a hard stop on bad packages.
    /// </summary>
    public static ValidationReport ValidateAndAssertValid(PackageModel package)
    {
        var report = ModelValidator.Inspect(package);
        if (!report.IsValid)
        {
            var errors = string.Join(Environment.NewLine,
                report.Errors.Select(e => $"  {e.RuleId}: {e.Message}"));
            throw new InvalidOperationException($"Package validation failed:{Environment.NewLine}{errors}");
        }
        return report;
    }
}
