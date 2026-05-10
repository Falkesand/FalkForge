using System.Collections.Frozen;
using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for the rule-engine flags surfaced by <c>forge validate</c>:
/// <c>--ignore</c>, <c>--warn-as-error</c>, and <c>--stop-on-first-error</c>.
///
/// These tests verify <see cref="ValidationOptions"/> behaviour by calling
/// <see cref="ModelValidator.Inspect"/> directly with crafted <see cref="PackageModel"/>
/// instances that produce known violations. Using the engine directly (rather than going
/// through <see cref="JsonConfigLoader"/>) is more reliable because the JSON loader has
/// its own early-exit guards (JSN002/JSN003) that fire before <see cref="ModelValidator"/>
/// is reached when required product fields are blank.
/// </summary>
public sealed class ValidateCommandRuleOptionsTests
{
    // ── model factories ───────────────────────────────────────────────────────

    /// <summary>
    /// A package with blank Name and Manufacturer → fires PKG001 + PKG002 (both Error).
    /// All other required fields are valid so no other violations fire from the top-level
    /// metadata rules.
    /// </summary>
    private static PackageModel TwoErrorModel() => new()
    {
        Name         = string.Empty,   // PKG001 Error
        Manufacturer = string.Empty,   // PKG002 Error
        Version      = new Version(1, 0, 0),
        UpgradeCode  = Guid.NewGuid(),
        ProductCode  = Guid.NewGuid(),
    };

    /// <summary>
    /// A package with valid metadata but Version 0.0.0 → fires PKG004 (Warning) only.
    /// Non-empty GUIDs ensure PKG009/PKG010 don't fire.
    /// </summary>
    private static PackageModel WarningOnlyModel() => new()
    {
        Name         = "Test Product",
        Manufacturer = "Test Manufacturer",
        Version      = new Version(0, 0, 0),   // PKG004 Warning
        UpgradeCode  = Guid.NewGuid(),
        ProductCode  = Guid.NewGuid(),
    };

    // ── --ignore ──────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_ignore_suppresses_named_rule()
    {
        // TwoErrorModel triggers PKG001 + PKG002. Suppressing PKG001 must remove it from report.
        var options = new ValidationOptions
        {
            IgnoredRules = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "PKG001")
        };

        var report = ModelValidator.Inspect(TwoErrorModel(), options);

        Assert.False(
            report.Violations.Any(v => v.RuleId.Value == "PKG001"),
            "PKG001 must be suppressed by IgnoredRules.");

        Assert.True(
            report.Violations.Any(v => v.RuleId.Value == "PKG002"),
            "PKG002 must still fire when only PKG001 is ignored.");
    }

    // ── --warn-as-error ───────────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_warn_as_error_promotes_warning()
    {
        // Baseline: PKG004 is Warning → report IsValid (no errors).
        var baseline = ModelValidator.Inspect(WarningOnlyModel());
        Assert.True(baseline.IsValid, "Baseline: PKG004 is a warning, so report must be valid.");
        Assert.True(
            baseline.Warnings.Any(v => v.RuleId.Value == "PKG004"),
            "Baseline: PKG004 warning must be present.");

        // With WarningsAsErrors: PKG004 promoted to Error → report invalid.
        var promoted = ModelValidator.Inspect(WarningOnlyModel(), new ValidationOptions { WarningsAsErrors = true });
        Assert.False(promoted.IsValid, "With WarningsAsErrors: PKG004 promoted to Error → report not valid.");
        Assert.True(
            promoted.Errors.Any(v => v.RuleId.Value == "PKG004"),
            "With WarningsAsErrors: PKG004 must appear as an error.");
    }

    // ── --stop-on-first-error ─────────────────────────────────────────────────

    [Fact]
    public void ValidateCommand_stop_on_first_error_returns_one_error()
    {
        // TwoErrorModel triggers PKG001 + PKG002 (and possibly more). With StopOnFirstError
        // the engine must stop after the first error — exactly one error in report.
        var options = new ValidationOptions { StopOnFirstError = true };

        var report = ModelValidator.Inspect(TwoErrorModel(), options);

        Assert.False(report.IsValid, "Report must be invalid (has errors).");
        Assert.Single(report.Errors);
    }
}
