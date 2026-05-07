using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for FeatureRules (FEA001-005).
/// Each test calls a rule directly via RuleContext.ForTest — no full orchestrator.
/// </summary>
public sealed class FeatureRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel Pkg(params FeatureModel[] features) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Features = features.ToList()
    };

    private static FeatureModel Feature(string id = "Feature1", string title = "Feature One") => new()
    {
        Id = id,
        Title = title
    };

    // ── FEA001 — Feature Id required ────────────────────────────────────────

    [Fact]
    public void Fea001_empty_id_yields_error()
    {
        var pkg = Pkg(new FeatureModel { Id = "", Title = "A" });
        var violations = FeatureRules.Fea001_IdRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FEA001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Fea001_whitespace_id_yields_error()
    {
        var pkg = Pkg(new FeatureModel { Id = "   ", Title = "A" });
        var violations = FeatureRules.Fea001_IdRequired.Evaluate(Ctx(pkg)).ToList();
        Assert.Single(violations);
    }

    [Fact]
    public void Fea001_valid_id_yields_no_violations()
    {
        Assert.Empty(FeatureRules.Fea001_IdRequired.Evaluate(Ctx(Pkg(Feature()))));
    }

    [Fact]
    public void Fea001_nested_feature_with_empty_id_yields_error()
    {
        var parent = new FeatureModel
        {
            Id = "Parent",
            Title = "Parent",
            Children = [new FeatureModel { Id = "", Title = "Child" }]
        };
        var violations = FeatureRules.Fea001_IdRequired.Evaluate(Ctx(Pkg(parent))).ToList();
        Assert.Single(violations);
    }

    // ── FEA002 — Feature Id unique ───────────────────────────────────────────

    [Fact]
    public void Fea002_duplicate_id_at_root_yields_error()
    {
        var pkg = Pkg(Feature("F1"), Feature("F1"));
        var violations = FeatureRules.Fea002_IdUnique.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FEA002", violations[0].RuleId.Value);
        Assert.Contains("F1", violations[0].Message);
    }

    [Fact]
    public void Fea002_duplicate_id_in_nested_child_yields_error()
    {
        var parent = new FeatureModel
        {
            Id = "Parent",
            Title = "Parent",
            Children = [Feature("Child"), Feature("Child")]
        };
        var violations = FeatureRules.Fea002_IdUnique.Evaluate(Ctx(Pkg(parent))).ToList();
        Assert.Single(violations);
    }

    [Fact]
    public void Fea002_unique_ids_across_tree_yields_no_violations()
    {
        var parent = new FeatureModel
        {
            Id = "Parent",
            Title = "Parent",
            Children = [Feature("Child1"), Feature("Child2")]
        };
        Assert.Empty(FeatureRules.Fea002_IdUnique.Evaluate(Ctx(Pkg(parent))));
    }

    // ── FEA003 — Feature Title required ─────────────────────────────────────

    [Fact]
    public void Fea003_empty_title_yields_error()
    {
        var pkg = Pkg(new FeatureModel { Id = "F1", Title = "" });
        var violations = FeatureRules.Fea003_TitleRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FEA003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Fea003_whitespace_title_yields_error()
    {
        var pkg = Pkg(new FeatureModel { Id = "F1", Title = "  " });
        Assert.Single(FeatureRules.Fea003_TitleRequired.Evaluate(Ctx(pkg)).ToList());
    }

    [Fact]
    public void Fea003_valid_title_yields_no_violations()
    {
        Assert.Empty(FeatureRules.Fea003_TitleRequired.Evaluate(Ctx(Pkg(Feature()))));
    }

    // ── FEA004 — Feature condition expression required ───────────────────────

    [Fact]
    public void Fea004_empty_condition_string_yields_error()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = "", Level = 1 }]
        });
        var violations = FeatureRules.Fea004_ConditionRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FEA004", violations[0].RuleId.Value);
    }

    [Fact]
    public void Fea004_null_condition_string_yields_error()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = null!, Level = 1 }]
        });
        Assert.Single(FeatureRules.Fea004_ConditionRequired.Evaluate(Ctx(pkg)).ToList());
    }

    [Fact]
    public void Fea004_valid_condition_yields_no_violations()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = "INSTALL=\"ALL\"", Level = 1 }]
        });
        Assert.Empty(FeatureRules.Fea004_ConditionRequired.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Fea004_no_conditions_yields_no_violations()
    {
        Assert.Empty(FeatureRules.Fea004_ConditionRequired.Evaluate(Ctx(Pkg(Feature()))));
    }

    // ── FEA005 — Feature condition level non-negative ────────────────────────

    [Fact]
    public void Fea005_negative_condition_level_yields_warning()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = "INSTALL", Level = -1 }]
        });
        var violations = FeatureRules.Fea005_ConditionLevelNonNegative.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FEA005", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Fea005_zero_level_yields_no_violations()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = "INSTALL", Level = 0 }]
        });
        Assert.Empty(FeatureRules.Fea005_ConditionLevelNonNegative.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Fea005_positive_level_yields_no_violations()
    {
        var pkg = Pkg(new FeatureModel
        {
            Id = "F1",
            Title = "F1",
            Conditions = [new FeatureConditionModel { Condition = "INSTALL", Level = 2 }]
        });
        Assert.Empty(FeatureRules.Fea005_ConditionLevelNonNegative.Evaluate(Ctx(pkg)));
    }
}
