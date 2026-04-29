using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class RegionPolicyTests
{
    [Fact]
    public void Absolute_value_is_declared()
    {
        Assert.True(System.Enum.IsDefined(typeof(RegionPolicy), RegionPolicy.Absolute));
    }

    [Fact]
    public void RightPacked_value_is_declared()
    {
        Assert.True(System.Enum.IsDefined(typeof(RegionPolicy), RegionPolicy.RightPacked));
    }

    [Fact]
    public void TopStacked_value_is_declared()
    {
        Assert.True(System.Enum.IsDefined(typeof(RegionPolicy), RegionPolicy.TopStacked));
    }

    [Fact]
    public void SingleControl_value_is_declared()
    {
        Assert.True(System.Enum.IsDefined(typeof(RegionPolicy), RegionPolicy.SingleControl));
    }

    [Fact]
    public void Enum_has_exactly_four_named_values()
    {
        var values = System.Enum.GetValues<RegionPolicy>();
        Assert.Equal(4, values.Length);
    }
}
