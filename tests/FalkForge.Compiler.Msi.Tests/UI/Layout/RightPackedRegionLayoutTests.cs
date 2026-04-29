using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class RightPackedRegionLayoutTests
{
    private static DialogRegion ButtonRow(RegionDefaults? defaults = null) => new()
    {
        Name = "ButtonRow",
        Bounds = new Rect { X = 0, Y = 243, Width = 360, Height = 17 },
        Policy = RegionPolicy.RightPacked,
        Defaults = defaults ?? new RegionDefaults { ChildWidth = 56, ChildHeight = 17, Gap = 8 },
    };

    private static PlacedControl Button(string name) =>
        new() { Name = name, Type = "PushButton" };

    [Fact]
    public void Empty_controls_returns_empty_array()
    {
        var policy = new RightPackedRegionLayout();

        var result = policy.Resolve(ButtonRow(), ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Three_buttons_with_uniform_gap_8_produce_X_304_240_176()
    {
        var policy = new RightPackedRegionLayout();
        var controls = ImmutableArray.Create(Button("Cancel"), Button("Next"), Button("Back"));

        var result = policy.Resolve(ButtonRow(), controls);

        Assert.Equal(3, result.Length);
        Assert.Equal(304, result[0].Bounds.X);
        Assert.Equal(240, result[1].Bounds.X);
        Assert.Equal(176, result[2].Bounds.X);
    }

    [Fact]
    public void Three_buttons_with_per_child_gaps_12_0_produce_X_304_236_180()
    {
        var policy = new RightPackedRegionLayout();
        var defaults = new RegionDefaults
        {
            ChildWidth = 56,
            ChildHeight = 17,
            Gap = 8,
            Gaps = ImmutableArray.Create(12, 0),
        };
        var controls = ImmutableArray.Create(Button("Cancel"), Button("Next"), Button("Back"));

        var result = policy.Resolve(ButtonRow(defaults), controls);

        Assert.Equal(3, result.Length);
        Assert.Equal(304, result[0].Bounds.X);
        Assert.Equal(236, result[1].Bounds.X);
        Assert.Equal(180, result[2].Bounds.X);
    }

    [Fact]
    public void Per_control_width_override_is_respected()
    {
        var policy = new RightPackedRegionLayout();
        var wide = new PlacedControl { Name = "Big", Type = "PushButton", OverrideWidth = 80 };
        var normal = Button("Next");

        var result = policy.Resolve(ButtonRow(), ImmutableArray.Create(wide, normal));

        // wide rightmost: X = 360 - 80 = 280; width 80
        Assert.Equal(280, result[0].Bounds.X);
        Assert.Equal(80, result[0].Bounds.Width);

        // next: X = 280 - 8 - 56 = 216; width 56
        Assert.Equal(216, result[1].Bounds.X);
        Assert.Equal(56, result[1].Bounds.Width);
    }

    [Fact]
    public void Y_matches_region_Y_for_every_button()
    {
        var policy = new RightPackedRegionLayout();
        var controls = ImmutableArray.Create(Button("A"), Button("B"), Button("C"));

        var result = policy.Resolve(ButtonRow(), controls);

        foreach (var placement in result)
        {
            Assert.Equal(243, placement.Bounds.Y);
            Assert.Equal(17, placement.Bounds.Height);
        }
    }
}
