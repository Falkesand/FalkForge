using System.Collections.Immutable;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class ValidationEngineTests
{
    private static PackageModel ValidPackage() => InstallerTestHost.BuildPackage(p =>
    {
        p.Name = "App";
        p.Manufacturer = "Corp";
        p.Version = new Version(1, 0, 0);
        p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
    });

    [Fact]
    public void Empty_registry_produces_valid_empty_report()
    {
        var engine = new ValidationEngine(new RuleRegistry(ImmutableArray<ValidationRule>.Empty));
        var package = ValidPackage();

        var report = engine.Run(package);

        Assert.True(report.IsValid);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void Engine_runs_rules_and_collects_violations()
    {
        var rule = new ValidationRule(
            new RuleId("TST001"),
            Severity.Error,
            ModelSection.Package,
            "Test rule",
            "Always fails",
            static ctx => [new Violation(new RuleId("TST001"), Severity.Error, ModelPath.Root, "always fails")]);

        var engine = new ValidationEngine(new RuleRegistry([rule]));

        var report = engine.Run(ValidPackage());

        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal("TST001", report.Errors.First().RuleId.Value);
    }

    [Fact]
    public void Engine_with_IgnoredRules_suppresses_matching_violations()
    {
        var rule = new ValidationRule(
            new RuleId("TST001"),
            Severity.Error,
            ModelSection.Package,
            "Test rule",
            "Always fails",
            static ctx => [new Violation(new RuleId("TST001"), Severity.Error, ModelPath.Root, "always fails")]);

        var engine = new ValidationEngine(new RuleRegistry([rule]));
        var options = ValidationOptions.Default with { IgnoredRules = new HashSet<string>(["TST001"]) };

        var report = engine.Run(ValidPackage(), options);

        Assert.True(report.IsValid);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void Engine_with_WarningsAsErrors_promotes_warnings()
    {
        var rule = new ValidationRule(
            new RuleId("TST002"),
            Severity.Warning,
            ModelSection.Package,
            "Warning rule",
            "Always warns",
            static ctx => [new Violation(new RuleId("TST002"), Severity.Warning, ModelPath.Root, "always warns")]);

        var engine = new ValidationEngine(new RuleRegistry([rule]));
        var options = ValidationOptions.Default with { WarningsAsErrors = true };

        var report = engine.Run(ValidPackage(), options);

        Assert.False(report.IsValid);
        Assert.Single(report.Errors);
        Assert.Equal(Severity.Error, report.Errors.First().Severity);
    }

    [Fact]
    public void Engine_aggregates_violations_from_multiple_rules()
    {
        var rule1 = new ValidationRule(
            new RuleId("TST001"), Severity.Error, ModelSection.Package, "R1", "D1",
            static ctx => [new Violation(new RuleId("TST001"), Severity.Error, ModelPath.Root, "msg1")]);
        var rule2 = new ValidationRule(
            new RuleId("TST002"), Severity.Warning, ModelSection.Package, "R2", "D2",
            static ctx => [new Violation(new RuleId("TST002"), Severity.Warning, ModelPath.Root, "msg2")]);

        var engine = new ValidationEngine(new RuleRegistry([rule1, rule2]));

        var report = engine.Run(ValidPackage());

        Assert.Equal(2, report.Violations.Length);
    }
}
