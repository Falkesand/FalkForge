using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class SingleControlRegionLayoutTests
{
    // Arbitrary policy-testing geometry — deliberately not a real region's coordinates, so this
    // fixture cannot be misread as documenting any stock layout's actual bounds.
    private static DialogRegion TestRegion() => new()
    {
        Name = "TestRegion",
        Bounds = new Rect { X = 0, Y = 0, Width = 370, Height = 40 },
        Policy = RegionPolicy.SingleControl,
    };

    [Fact]
    public void One_control_fills_region_bounds()
    {
        var policy = new SingleControlRegionLayout();
        var control = new PlacedControl { Name = "BannerImage", Type = "Bitmap" };

        var result = policy.Resolve(TestRegion(), ImmutableArray.Create(control));

        var placement = Assert.Single(result);
        Assert.Same(control, placement.Source);
        Assert.Equal(0, placement.Bounds.X);
        Assert.Equal(0, placement.Bounds.Y);
        Assert.Equal(370, placement.Bounds.Width);
        Assert.Equal(40, placement.Bounds.Height);
    }

    [Fact]
    public void Two_controls_throws_InvalidOperationException()
    {
        var policy = new SingleControlRegionLayout();
        var a = new PlacedControl { Name = "A", Type = "Bitmap" };
        var b = new PlacedControl { Name = "B", Type = "Bitmap" };

        Assert.Throws<InvalidOperationException>(
            () => policy.Resolve(TestRegion(), ImmutableArray.Create(a, b)));
    }

    [Fact]
    public void Zero_controls_returns_empty_array()
    {
        var policy = new SingleControlRegionLayout();

        var result = policy.Resolve(TestRegion(), ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }
}
