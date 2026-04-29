using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class TopStackedRegionLayoutTests
{
    private static DialogRegion Stack(int height = 100, RegionDefaults? defaults = null) => new()
    {
        Name = "Stack",
        Bounds = new Rect { X = 0, Y = 0, Width = 200, Height = height },
        Policy = RegionPolicy.TopStacked,
        Defaults = defaults ?? new RegionDefaults { ChildWidth = 200, ChildHeight = 17, Gap = 8 },
    };

    private static PlacedControl Item(string name) =>
        new() { Name = name, Type = "Text" };

    [Fact]
    public void Empty_controls_returns_empty_array()
    {
        var policy = new TopStackedRegionLayout();

        var result = policy.Resolve(Stack(), ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Three_controls_with_default_metrics_produce_Y_0_25_50()
    {
        var policy = new TopStackedRegionLayout();
        var controls = ImmutableArray.Create(Item("A"), Item("B"), Item("C"));

        var result = policy.Resolve(Stack(), controls);

        Assert.Equal(3, result.Length);
        Assert.Equal(0, result[0].Bounds.Y);
        Assert.Equal(25, result[1].Bounds.Y);
        Assert.Equal(50, result[2].Bounds.Y);
    }

    [Fact]
    public void Default_X_matches_region_X_and_width_matches_region_width()
    {
        var policy = new TopStackedRegionLayout();
        var region = new DialogRegion
        {
            Name = "Stack",
            Bounds = new Rect { X = 25, Y = 60, Width = 320, Height = 100 },
            Policy = RegionPolicy.TopStacked,
        };
        var control = Item("A");

        var result = policy.Resolve(region, ImmutableArray.Create(control));

        Assert.Equal(25, result[0].Bounds.X);
        Assert.Equal(320, result[0].Bounds.Width);
        Assert.Equal(17, result[0].Bounds.Height);
    }

    [Fact]
    public void Per_control_height_override_propagates_to_next_Y()
    {
        var policy = new TopStackedRegionLayout();
        var tall = new PlacedControl { Name = "Tall", Type = "Text", OverrideHeight = 30 };
        var normal = Item("Next");

        var result = policy.Resolve(Stack(), ImmutableArray.Create(tall, normal));

        Assert.Equal(0, result[0].Bounds.Y);
        Assert.Equal(30, result[0].Bounds.Height);
        // Next Y = 0 + 30 + 8 = 38 (not 25)
        Assert.Equal(38, result[1].Bounds.Y);
    }

    [Fact]
    public void Per_control_width_override_is_respected()
    {
        var policy = new TopStackedRegionLayout();
        var narrow = new PlacedControl { Name = "Narrow", Type = "Text", OverrideWidth = 100 };

        var result = policy.Resolve(Stack(), ImmutableArray.Create(narrow));

        Assert.Equal(100, result[0].Bounds.Width);
    }
}
