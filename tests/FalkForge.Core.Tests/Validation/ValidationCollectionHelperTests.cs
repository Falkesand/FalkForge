using System.Collections.Immutable;
using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Focused unit tests for ValidationCollectionHelper.ValidateCollection&lt;T&gt;.
/// Verifies the helper correctly accumulates violations and skips null results.
/// </summary>
public sealed class ValidationCollectionHelperTests
{
    private static Violation MakeViolation(string ruleId, int index) =>
        new(new RuleId(ruleId), Severity.Error,
            ModelPath.Root.Field("Items").Index(index),
            $"Item at {index} is invalid.");

    [Fact]
    public void ValidateCollection_empty_list_returns_empty()
    {
        var result = ValidationCollectionHelper.ValidateCollection<string>(
            [],
            static (_, _) => null);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateCollection_all_pass_returns_empty()
    {
        var items = new[] { "a", "b", "c" };

        var result = ValidationCollectionHelper.ValidateCollection(
            items,
            static (_, _) => null);

        Assert.Empty(result);
    }

    [Fact]
    public void ValidateCollection_all_fail_returns_one_violation_per_item()
    {
        var items = new[] { "x", "y" };

        var result = ValidationCollectionHelper.ValidateCollection(
            items,
            static (_, i) => new Violation(new RuleId("T001"), Severity.Error,
                ModelPath.Root.Field("Items").Index(i), "bad"));

        Assert.Equal(2, result.Length);
        Assert.Equal("T001", result[0].RuleId.Value);
        Assert.Equal("T001", result[1].RuleId.Value);
        // Paths differ: index 0 vs index 1
        Assert.NotEqual(result[0].Path, result[1].Path);
    }

    [Fact]
    public void ValidateCollection_partial_fail_returns_only_failing_violations()
    {
        var items = new[] { "ok", "", "ok", "" };

        var result = ValidationCollectionHelper.ValidateCollection(
            items,
            static (item, i) => string.IsNullOrEmpty(item)
                ? new Violation(new RuleId("T002"), Severity.Error,
                    ModelPath.Root.Field("Items").Index(i), "empty")
                : null);

        Assert.Equal(2, result.Length);
        Assert.Equal("T002", result[0].RuleId.Value);
        Assert.Equal("T002", result[1].RuleId.Value);
    }

    [Fact]
    public void ValidateCollection_preserves_insertion_order()
    {
        var items = new[] { "c", "b", "a" };
        var seen = new List<int>();

        ValidationCollectionHelper.ValidateCollection(
            items,
            (_, i) => { seen.Add(i); return null; });

        Assert.Equal([0, 1, 2], seen);
    }

    [Fact]
    public void ValidateCollection_returns_immutable_array()
    {
        var result = ValidationCollectionHelper.ValidateCollection<string>(
            ["x"],
            static (_, i) => new Violation(new RuleId("T003"), Severity.Warning,
                ModelPath.Root.Index(i), "w"));

        Assert.IsType<ImmutableArray<Violation>>(result);
    }
}
