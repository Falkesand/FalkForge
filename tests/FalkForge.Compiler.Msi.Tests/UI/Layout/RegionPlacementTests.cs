using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class RegionPlacementTests
{
    [Fact]
    public void Construct_with_region_and_controls_succeeds()
    {
        var control = new PlacedControl { Name = "Next", Type = "PushButton" };
        var placement = new RegionPlacement
        {
            RegionName = "ButtonRow",
            Controls = ImmutableArray.Create(control),
        };

        Assert.Equal("ButtonRow", placement.RegionName);
        Assert.Single(placement.Controls);
        Assert.Equal("Next", placement.Controls[0].Name);
    }

    [Fact]
    public void Default_controls_is_empty_immutable_array()
    {
        var placement = new RegionPlacement
        {
            RegionName = "Banner",
        };

        Assert.False(placement.Controls.IsDefault);
        Assert.True(placement.Controls.IsEmpty);
    }

    [Fact]
    public void With_expression_replaces_controls()
    {
        var first = new PlacedControl { Name = "Next", Type = "PushButton" };
        var second = new PlacedControl { Name = "Cancel", Type = "PushButton" };
        var placement = new RegionPlacement
        {
            RegionName = "ButtonRow",
            Controls = ImmutableArray.Create(first),
        };

        var updated = placement with { Controls = ImmutableArray.Create(first, second) };

        Assert.Equal(2, updated.Controls.Length);
        Assert.Single(placement.Controls);
    }
}
