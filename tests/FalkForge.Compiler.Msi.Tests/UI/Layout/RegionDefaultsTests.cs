using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class RegionDefaultsTests
{
    [Fact]
    public void Default_construction_uses_msi_button_metrics()
    {
        var defaults = new RegionDefaults();

        Assert.Equal(56, defaults.ChildWidth);
        Assert.Equal(17, defaults.ChildHeight);
        Assert.Equal(8, defaults.Gap);
        Assert.True(defaults.Gaps.IsEmpty);
    }

    [Fact]
    public void Construct_with_custom_metrics_preserves_values()
    {
        var defaults = new RegionDefaults
        {
            ChildWidth = 80,
            ChildHeight = 22,
            Gap = 4,
            Gaps = ImmutableArray.Create(2, 6, 4),
        };

        Assert.Equal(80, defaults.ChildWidth);
        Assert.Equal(22, defaults.ChildHeight);
        Assert.Equal(4, defaults.Gap);
        Assert.Equal(new[] { 2, 6, 4 }, defaults.Gaps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Negative_or_zero_child_width_throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionDefaults { ChildWidth = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Negative_or_zero_child_height_throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionDefaults { ChildHeight = value });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Negative_or_zero_gap_throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionDefaults { Gap = value });
    }

    [Fact]
    public void Negative_gap_in_gaps_array_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegionDefaults
        {
            Gaps = ImmutableArray.Create(4, -1, 2),
        });
    }

    [Fact]
    public void Empty_gaps_array_is_allowed()
    {
        var defaults = new RegionDefaults { Gaps = ImmutableArray<int>.Empty };

        Assert.True(defaults.Gaps.IsEmpty);
    }

    [Fact]
    public void Zero_gap_in_gaps_array_is_allowed()
    {
        var defaults = new RegionDefaults { Gaps = ImmutableArray.Create(0, 0, 0) };

        Assert.Equal(3, defaults.Gaps.Length);
    }
}
