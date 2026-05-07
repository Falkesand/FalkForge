using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Tests for the new ModelValidator.Check and ModelValidator.Inspect facade
/// methods added in RFC cycle-3. The legacy Validate() method is tested separately
/// in ModelValidatorTests.cs and remains untouched during this RFC.
/// </summary>
public sealed class ModelValidatorFacadeTests
{
    private static PackageModel ValidPackage() => InstallerTestHost.BuildPackage(p =>
    {
        p.Name = "App";
        p.Manufacturer = "Corp";
        p.Version = new Version(1, 0, 0);
        p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
    });

    private static PackageModel InvalidPackage() => new()
    {
        Name = "",
        Manufacturer = "",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid()
    };

    // ── Check ────────────────────────────────────────────────────────────────

    [Fact]
    public void Check_returns_success_on_valid_package()
    {
        var result = ModelValidator.Check(ValidPackage());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Check_returns_failure_on_invalid_package()
    {
        var result = ModelValidator.Check(InvalidPackage());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Check_failure_message_contains_rule_code()
    {
        var result = ModelValidator.Check(InvalidPackage());

        Assert.Contains("PKG001", result.Error.Message);
    }

    [Fact]
    public void Check_failure_error_kind_is_validation()
    {
        var result = ModelValidator.Check(InvalidPackage());

        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    // ── Inspect ──────────────────────────────────────────────────────────────

    [Fact]
    public void Inspect_returns_valid_report_for_valid_package()
    {
        var report = ModelValidator.Inspect(ValidPackage());

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Inspect_returns_invalid_report_for_invalid_package()
    {
        var report = ModelValidator.Inspect(InvalidPackage());

        Assert.False(report.IsValid);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public void Inspect_returns_warnings_for_zero_version()
    {
        var pkg = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(0, 0, 0),
            UpgradeCode = Guid.NewGuid(), ProductCode = Guid.NewGuid()
        };

        var report = ModelValidator.Inspect(pkg);

        Assert.Contains(report.Warnings, w => w.RuleId.Value == "PKG004");
    }

    [Fact]
    public void Check_and_Inspect_produce_identical_error_message()
    {
        var pkg = InvalidPackage();
        var checkResult = ModelValidator.Check(pkg);
        var inspectResult = ModelValidator.Inspect(pkg).ToResult();

        Assert.Equal(checkResult.Error.Message, inspectResult.Error.Message);
    }

    // ── ListRules ────────────────────────────────────────────────────────────

    [Fact]
    public void ListRules_returns_all_package_rules()
    {
        var rules = ModelValidator.ListRules(ValidationTarget.Package);

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.Id.Value == "PKG001");
    }

    [Fact]
    public void ListRules_returns_typed_ValidationRule_with_metadata()
    {
        var rules = ModelValidator.ListRules(ValidationTarget.Package);
        var pkg001 = rules.First(r => r.Id.Value == "PKG001");

        Assert.Equal(Severity.Error, pkg001.Severity);
        Assert.Equal(ModelSection.Package, pkg001.Section);
        Assert.False(string.IsNullOrWhiteSpace(pkg001.Title));
        Assert.False(string.IsNullOrWhiteSpace(pkg001.Description));
    }
}
