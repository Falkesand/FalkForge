using System.Collections.Immutable;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class RuleRegistryTests
{
    private static ValidationRule MakeRule(string id, Severity severity = Severity.Error)
        => new(
            new RuleId(id),
            severity,
            ModelSection.Package,
            $"Title {id}",
            $"Description {id}",
            static _ => []);

    [Fact]
    public void Empty_registry_has_no_rules()
    {
        var registry = new RuleRegistry(ImmutableArray<ValidationRule>.Empty);

        Assert.Empty(registry.Rules);
    }

    [Fact]
    public void Find_returns_rule_by_id()
    {
        var rule = MakeRule("PKG001");
        var registry = new RuleRegistry([rule]);

        var found = registry.Find(new RuleId("PKG001"));

        Assert.NotNull(found);
        Assert.Equal("PKG001", found!.Id.Value);
    }

    [Fact]
    public void Find_returns_null_for_missing_id()
    {
        var registry = new RuleRegistry(ImmutableArray<ValidationRule>.Empty);

        Assert.Null(registry.Find(new RuleId("PKG001")));
    }

    [Fact]
    public void Without_removes_specified_rule()
    {
        var registry = new RuleRegistry([MakeRule("PKG001"), MakeRule("PKG002")]);

        var filtered = registry.Without(new RuleId("PKG001"));

        Assert.Null(filtered.Find(new RuleId("PKG001")));
        Assert.NotNull(filtered.Find(new RuleId("PKG002")));
    }

    [Fact]
    public void Without_returns_new_registry_does_not_mutate_original()
    {
        var registry = new RuleRegistry([MakeRule("PKG001")]);

        _ = registry.Without(new RuleId("PKG001"));

        Assert.NotNull(registry.Find(new RuleId("PKG001")));
    }

    [Fact]
    public void WithAdded_includes_new_rule()
    {
        var registry = new RuleRegistry([MakeRule("PKG001")]);
        var extra = MakeRule("EXT001");

        var extended = registry.WithAdded(extra);

        Assert.NotNull(extended.Find(new RuleId("EXT001")));
        Assert.NotNull(extended.Find(new RuleId("PKG001")));
    }

    [Fact]
    public void OverrideSeverity_changes_severity_of_rule()
    {
        var registry = new RuleRegistry([MakeRule("PKG004", Severity.Warning)]);

        var overridden = registry.OverrideSeverity(new RuleId("PKG004"), Severity.Error);

        Assert.Equal(Severity.Error, overridden.Find(new RuleId("PKG004"))!.Severity);
    }

    [Fact]
    public void OverrideSeverity_returns_new_registry_does_not_mutate_original()
    {
        var registry = new RuleRegistry([MakeRule("PKG004", Severity.Warning)]);

        _ = registry.OverrideSeverity(new RuleId("PKG004"), Severity.Error);

        Assert.Equal(Severity.Warning, registry.Find(new RuleId("PKG004"))!.Severity);
    }

    [Fact]
    public void FilterSection_returns_only_matching_section()
    {
        var registry = new RuleRegistry([
            MakeRule("PKG001"),
            new ValidationRule(new RuleId("SVC001"), Severity.Error, ModelSection.Service, "T", "D", static _ => [])
        ]);

        var filtered = registry.FilterSection(ModelSection.Service);

        Assert.Single(filtered.Rules);
        Assert.Equal("SVC001", filtered.Rules[0].Id.Value);
    }

    [Fact]
    public void ById_provides_O1_lookup()
    {
        var rules = Enumerable.Range(1, 50)
            .Select(i => MakeRule($"PKG{i:D3}"))
            .ToImmutableArray();
        var registry = new RuleRegistry(rules);

        // FrozenDictionary — just verify all 50 are findable
        for (var i = 1; i <= 50; i++)
            Assert.NotNull(registry.ById.GetValueOrDefault(new RuleId($"PKG{i:D3}")));
    }
}
