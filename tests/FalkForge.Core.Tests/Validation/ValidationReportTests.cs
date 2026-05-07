using System.Collections.Immutable;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class ValidationReportTests
{
    private static Violation Error(string code, string msg = "error")
        => new(new RuleId(code), Severity.Error, ModelPath.Root, msg);

    private static Violation Warning(string code, string msg = "warning")
        => new(new RuleId(code), Severity.Warning, ModelPath.Root, msg);

    [Fact]
    public void IsValid_true_when_no_errors()
    {
        var report = new ValidationReport(ImmutableArray<Violation>.Empty);

        Assert.True(report.IsValid);
    }

    [Fact]
    public void IsValid_false_when_any_error_present()
    {
        var report = new ValidationReport([Error("PKG001")]);

        Assert.False(report.IsValid);
    }

    [Fact]
    public void IsValid_true_when_only_warnings_present()
    {
        var report = new ValidationReport([Warning("PKG004")]);

        Assert.True(report.IsValid);
    }

    [Fact]
    public void Errors_returns_only_error_violations()
    {
        var report = new ValidationReport([Error("PKG001"), Warning("PKG004"), Error("PKG002")]);

        Assert.Equal(2, report.Errors.Count());
        Assert.All(report.Errors, v => Assert.Equal(Severity.Error, v.Severity));
    }

    [Fact]
    public void Warnings_returns_only_warning_violations()
    {
        var report = new ValidationReport([Error("PKG001"), Warning("PKG004")]);

        Assert.Single(report.Warnings);
        Assert.Equal("PKG004", report.Warnings.First().RuleId.Value);
    }

    [Fact]
    public void ToResult_returns_success_when_valid()
    {
        var report = new ValidationReport(ImmutableArray<Violation>.Empty);

        var result = report.ToResult();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ToResult_returns_failure_when_invalid()
    {
        var report = new ValidationReport([Error("PKG001", "Name required.")]);

        var result = report.ToResult();

        Assert.True(result.IsFailure);
        Assert.Contains("PKG001", result.Error.Message);
        Assert.Contains("Name required.", result.Error.Message);
    }

    [Fact]
    public void ToResult_aggregates_all_error_messages()
    {
        var report = new ValidationReport([Error("PKG001", "Name required."), Error("PKG002", "Manufacturer required.")]);

        var result = report.ToResult();

        Assert.Contains("PKG001", result.Error.Message);
        Assert.Contains("PKG002", result.Error.Message);
    }

    [Fact]
    public void ByRule_groups_violations_by_rule_id()
    {
        var report = new ValidationReport([Error("PKG001"), Error("PKG001"), Warning("PKG004")]);

        var byRule = report.ByRule();

        Assert.Equal(2, byRule["PKG001"].Count());
        Assert.Single(byRule["PKG004"]);
    }
}
