using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class IRegionLayoutPolicyTests
{
    private sealed class StubPolicy : IRegionLayoutPolicy
    {
        public ImmutableArray<ResolvedControlPlacement> Resolve(DialogRegion region, ImmutableArray<PlacedControl> controls)
            => ImmutableArray<ResolvedControlPlacement>.Empty;
    }

    [Fact]
    public void IRegionLayoutPolicy_can_be_implemented_and_returns_empty_array()
    {
        IRegionLayoutPolicy policy = new StubPolicy();
        var region = new DialogRegion
        {
            Name = "ButtonRow",
            Bounds = new Rect { X = 0, Y = 0, Width = 360, Height = 17 },
            Policy = RegionPolicy.RightPacked,
        };

        var result = policy.Resolve(region, ImmutableArray<PlacedControl>.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolvedControlPlacement_carries_source_and_bounds()
    {
        var control = new PlacedControl { Name = "Next", Type = "PushButton" };
        var bounds = new Rect { X = 240, Y = 243, Width = 56, Height = 17 };

        var placement = new ResolvedControlPlacement(control, bounds);

        Assert.Same(control, placement.Source);
        Assert.Equal(bounds, placement.Bounds);
    }
}
