using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class AbsoluteRegionLayoutTests
{
    private static DialogRegion ContentArea() => new()
    {
        Name = "ContentArea",
        Bounds = new Rect { X = 15, Y = 60, Width = 340, Height = 165 },
        Policy = RegionPolicy.Absolute,
    };

    [Fact]
    public void Empty_controls_returns_empty_array()
    {
        var policy = new AbsoluteRegionLayout();

        var result = policy.Resolve(ContentArea(), ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Control_with_all_overrides_uses_exact_bounds()
    {
        var policy = new AbsoluteRegionLayout();
        var control = new PlacedControl
        {
            Name = "Title",
            Type = "Text",
            OverrideX = 25,
            OverrideY = 23,
            OverrideWidth = 280,
            OverrideHeight = 20,
        };

        var result = policy.Resolve(ContentArea(), ImmutableArray.Create(control));

        var placement = Assert.Single(result);
        Assert.Same(control, placement.Source);
        Assert.Equal(25, placement.Bounds.X);
        Assert.Equal(23, placement.Bounds.Y);
        Assert.Equal(280, placement.Bounds.Width);
        Assert.Equal(20, placement.Bounds.Height);
    }

    [Fact]
    public void Control_with_no_overrides_falls_back_to_region_origin_and_defaults()
    {
        var policy = new AbsoluteRegionLayout();
        var control = new PlacedControl { Name = "Body", Type = "Text" };

        var result = policy.Resolve(ContentArea(), ImmutableArray.Create(control));

        var placement = Assert.Single(result);
        Assert.Equal(15, placement.Bounds.X);
        Assert.Equal(60, placement.Bounds.Y);
        Assert.Equal(56, placement.Bounds.Width);   // RegionDefaults.ChildWidth
        Assert.Equal(17, placement.Bounds.Height);  // RegionDefaults.ChildHeight
    }

    [Fact]
    public void Two_controls_preserve_input_order()
    {
        var policy = new AbsoluteRegionLayout();
        var first = new PlacedControl { Name = "One", Type = "Text", OverrideX = 10 };
        var second = new PlacedControl { Name = "Two", Type = "Text", OverrideX = 20 };

        var result = policy.Resolve(ContentArea(), ImmutableArray.Create(first, second));

        Assert.Equal(2, result.Length);
        Assert.Same(first, result[0].Source);
        Assert.Same(second, result[1].Source);
    }
}
