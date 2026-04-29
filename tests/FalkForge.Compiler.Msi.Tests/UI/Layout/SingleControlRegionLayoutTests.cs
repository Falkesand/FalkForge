using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class SingleControlRegionLayoutTests
{
    private static DialogRegion Banner() => new()
    {
        Name = "Banner",
        Bounds = new Rect { X = 0, Y = 0, Width = 370, Height = 58 },
        Policy = RegionPolicy.SingleControl,
    };

    [Fact]
    public void One_control_fills_region_bounds()
    {
        var policy = new SingleControlRegionLayout();
        var control = new PlacedControl { Name = "BannerImage", Type = "Bitmap" };

        var result = policy.Resolve(Banner(), ImmutableArray.Create(control));

        var placement = Assert.Single(result);
        Assert.Same(control, placement.Source);
        Assert.Equal(0, placement.Bounds.X);
        Assert.Equal(0, placement.Bounds.Y);
        Assert.Equal(370, placement.Bounds.Width);
        Assert.Equal(58, placement.Bounds.Height);
    }

    [Fact]
    public void Two_controls_throws_InvalidOperationException()
    {
        var policy = new SingleControlRegionLayout();
        var a = new PlacedControl { Name = "A", Type = "Bitmap" };
        var b = new PlacedControl { Name = "B", Type = "Bitmap" };

        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(Banner(), ImmutableArray.Create(a, b)));
    }

    [Fact]
    public void Zero_controls_returns_empty_array()
    {
        var policy = new SingleControlRegionLayout();

        var result = policy.Resolve(Banner(), ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }
}
