using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Tests for the new Check/Inspect engine-based API on the sub-model validators
/// (MergeModuleValidator, PatchValidator, TransformValidator).
/// Added in RFC cycle-3 slice-A.
/// </summary>
public sealed class SubValidatorEngineTests
{
    // ── MergeModuleValidator.Check ───────────────────────────────────────────

    [Fact]
    public void MergeModule_Check_returns_success_for_valid_model()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.NewGuid(),
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var result = MergeModuleValidator.Check(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void MergeModule_Check_returns_failure_for_empty_guid()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.Empty,
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var result = MergeModuleValidator.Check(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSM001", result.Error.Message);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void MergeModule_Inspect_returns_invalid_report_for_multiple_violations()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.Empty,
            Language = 0,
            Version = new Version(1, 0, 0),
            Manufacturer = ""
        };

        var report = MergeModuleValidator.Inspect(model);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSM001");
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSM003");
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSM004");
    }

    [Fact]
    public void MergeModule_Inspect_returns_valid_report_for_valid_model()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.NewGuid(),
            Language = 1033,
            Version = new Version(1, 0, 0),
            Manufacturer = "TestCorp"
        };

        var report = MergeModuleValidator.Inspect(model);

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void MergeModule_Check_and_Inspect_produce_same_rule_codes()
    {
        var model = new MergeModuleModel
        {
            Id = Guid.Empty,
            Language = 0,
            Version = new Version(1, 0, 0),
            Manufacturer = ""
        };

        var check = MergeModuleValidator.Check(model);
        var inspect = MergeModuleValidator.Inspect(model);

        Assert.True(check.IsFailure);
        Assert.False(inspect.IsValid);
        // All error codes from inspect are mentioned in check's message
        foreach (var v in inspect.Errors)
            Assert.Contains(v.RuleId.Value, check.Error.Message);
    }

    // ── PatchValidator.Check ─────────────────────────────────────────────────

    [Fact]
    public void Patch_Check_returns_success_for_valid_model()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Update,
            TargetMsiPath = @"C:\old.msi",
            UpdatedMsiPath = @"C:\new.msi"
        };

        var result = PatchValidator.Check(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Patch_Check_returns_failure_for_empty_id()
    {
        var model = new PatchModel
        {
            Id = Guid.Empty,
            Classification = PatchClassification.Update,
            TargetMsiPath = @"C:\old.msi",
            UpdatedMsiPath = @"C:\new.msi"
        };

        var result = PatchValidator.Check(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSP004", result.Error.Message);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Patch_Inspect_returns_all_violations_for_empty_model()
    {
        var model = new PatchModel
        {
            Id = Guid.Empty,
            Classification = PatchClassification.Update,
            TargetMsiPath = "",
            UpdatedMsiPath = ""
        };

        var report = PatchValidator.Inspect(model);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSP001");
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSP002");
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MSP004");
    }

    [Fact]
    public void Patch_Inspect_returns_valid_report_for_valid_model()
    {
        var model = new PatchModel
        {
            Id = Guid.NewGuid(),
            Classification = PatchClassification.Hotfix,
            TargetMsiPath = @"C:\old.msi",
            UpdatedMsiPath = @"C:\new.msi"
        };

        var report = PatchValidator.Inspect(model);

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    // ── TransformValidator.Check ─────────────────────────────────────────────

    [Fact]
    public void Transform_Check_returns_success_for_valid_model()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base.msi",
            TargetMsiPath = @"C:\target.msi"
        };

        var result = TransformValidator.Check(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Transform_Check_returns_failure_for_empty_paths()
    {
        var model = new TransformModel
        {
            BaseMsiPath = "",
            TargetMsiPath = ""
        };

        var result = TransformValidator.Check(model);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Transform_Inspect_returns_all_violations_for_empty_paths()
    {
        var model = new TransformModel
        {
            BaseMsiPath = "",
            TargetMsiPath = ""
        };

        var report = TransformValidator.Inspect(model);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MST001");
        Assert.Contains(report.Errors, v => v.RuleId.Value == "MST002");
    }

    [Fact]
    public void Transform_Inspect_returns_valid_report_for_valid_model()
    {
        var model = new TransformModel
        {
            BaseMsiPath = @"C:\base.msi",
            TargetMsiPath = @"C:\target.msi"
        };

        var report = TransformValidator.Inspect(model);

        Assert.True(report.IsValid);
        Assert.Empty(report.Errors);
    }

    // ── ListRules coverage for sub-targets ───────────────────────────────────

    [Fact]
    public void ListRules_MergeModule_contains_MSM_rules()
    {
        var rules = ModelValidator.ListRules(ValidationTarget.MergeModule);

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.Id.Value == "MSM001");
        Assert.Contains(rules, r => r.Id.Value == "MSM004");
    }

    [Fact]
    public void ListRules_Patch_contains_MSP_rules()
    {
        var rules = ModelValidator.ListRules(ValidationTarget.Patch);

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.Id.Value == "MSP001");
        Assert.Contains(rules, r => r.Id.Value == "MSP004");
    }

    [Fact]
    public void ListRules_Transform_contains_MST_rules()
    {
        var rules = ModelValidator.ListRules(ValidationTarget.Transform);

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => r.Id.Value == "MST001");
        Assert.Contains(rules, r => r.Id.Value == "MST002");
    }
}
