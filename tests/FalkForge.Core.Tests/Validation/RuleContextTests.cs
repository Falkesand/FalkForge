using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class RuleContextTests
{
    private static PackageModel Package(
        IReadOnlyList<FeatureModel>? features = null,
        IReadOnlyList<CustomTableModel>? customTables = null) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Features = features ?? [],
        CustomTables = customTables ?? []
    };

    [Fact]
    public void FeaturesById_is_bijection_for_known_feature_ids()
    {
        var pkg = Package(features:
        [
            new FeatureModel { Id = "Core", Title = "Core" },
            new FeatureModel { Id = "Extra", Title = "Extra" }
        ]);

        var ctx = RuleContext.ForTest(pkg);

        Assert.True(ctx.FeaturesById.ContainsKey("Core"));
        Assert.True(ctx.FeaturesById.ContainsKey("Extra"));
        Assert.Equal(2, ctx.FeaturesById.Count);
    }

    [Fact]
    public void FeaturesById_includes_nested_features()
    {
        var pkg = Package(features:
        [
            new FeatureModel
            {
                Id = "Root",
                Title = "Root",
                Children =
                [
                    new FeatureModel { Id = "Child", Title = "Child" }
                ]
            }
        ]);

        var ctx = RuleContext.ForTest(pkg);

        Assert.True(ctx.FeaturesById.ContainsKey("Root"));
        Assert.True(ctx.FeaturesById.ContainsKey("Child"));
    }

    [Fact]
    public void FeatureWalk_flattens_depth_first()
    {
        var pkg = Package(features:
        [
            new FeatureModel
            {
                Id = "A",
                Title = "A",
                Children =
                [
                    new FeatureModel { Id = "A1", Title = "A1" }
                ]
            },
            new FeatureModel { Id = "B", Title = "B" }
        ]);

        var ctx = RuleContext.ForTest(pkg);
        var ids = ctx.FeatureWalk.Select(e => e.Feature.Id).ToList();

        Assert.Equal(["A", "A1", "B"], ids);
    }

    [Fact]
    public void FeatureWalk_carries_correct_depth()
    {
        var pkg = Package(features:
        [
            new FeatureModel
            {
                Id = "Root",
                Title = "Root",
                Children =
                [
                    new FeatureModel { Id = "Child", Title = "Child" }
                ]
            }
        ]);

        var ctx = RuleContext.ForTest(pkg);

        Assert.Equal(0, ctx.FeatureWalk.First(e => e.Feature.Id == "Root").Depth);
        Assert.Equal(1, ctx.FeatureWalk.First(e => e.Feature.Id == "Child").Depth);
    }

    [Fact]
    public void CustomTablesByName_indexes_all_tables()
    {
        var pkg = Package(customTables:
        [
            new CustomTableModel { Name = "AppConfig", Columns = [new CustomTableColumnModel { Name = "Key", PrimaryKey = true, Type = CustomTableColumnType.String }] },
            new CustomTableModel { Name = "AppLog",    Columns = [new CustomTableColumnModel { Name = "Id",  PrimaryKey = true, Type = CustomTableColumnType.Int32  }] }
        ]);

        var ctx = RuleContext.ForTest(pkg);

        Assert.True(ctx.CustomTablesByName.ContainsKey("AppConfig"));
        Assert.True(ctx.CustomTablesByName.ContainsKey("AppLog"));
    }
}
