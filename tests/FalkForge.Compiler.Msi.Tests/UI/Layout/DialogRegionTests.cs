using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogRegionTests
{
    [Fact]
    public void Construct_with_valid_name_and_bounds_preserves_values()
    {
        var region = new DialogRegion
        {
            Name = "ButtonRow",
            Bounds = new Rect { X = 0, Y = 243, Width = 370, Height = 27 },
            Policy = RegionPolicy.RightPacked,
        };

        Assert.Equal("ButtonRow", region.Name);
        Assert.Equal(370, region.Bounds.Width);
        Assert.Equal(RegionPolicy.RightPacked, region.Policy);
        Assert.NotNull(region.Defaults);
    }

    [Fact]
    public void Default_defaults_are_assigned_when_not_set()
    {
        var region = new DialogRegion
        {
            Name = "Body",
            Bounds = new Rect { X = 0, Y = 0, Width = 100, Height = 50 },
            Policy = RegionPolicy.Absolute,
        };

        Assert.Equal(56, region.Defaults.ChildWidth);
        Assert.Equal(17, region.Defaults.ChildHeight);
    }

    [Fact]
    public void Empty_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogRegion
        {
            Name = string.Empty,
            Bounds = default,
            Policy = RegionPolicy.Absolute,
        });
    }

    [Fact]
    public void Whitespace_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogRegion
        {
            Name = "   ",
            Bounds = default,
            Policy = RegionPolicy.Absolute,
        });
    }

    [Fact]
    public void Identifier_with_digit_first_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogRegion
        {
            Name = "1Bad",
            Bounds = default,
            Policy = RegionPolicy.Absolute,
        });
    }

    [Theory]
    [InlineData("Bad-Name")]
    [InlineData("Bad Name")]
    [InlineData("Bad.Name")]
    [InlineData("Bad/Name")]
    public void Identifier_with_special_chars_throws(string name)
    {
        Assert.Throws<ArgumentException>(() => new DialogRegion
        {
            Name = name,
            Bounds = default,
            Policy = RegionPolicy.Absolute,
        });
    }

    [Theory]
    [InlineData("A")]
    [InlineData("_underscore")]
    [InlineData("Name_With_Underscores")]
    [InlineData("Name123")]
    public void Identifier_with_valid_chars_succeeds(string name)
    {
        var region = new DialogRegion
        {
            Name = name,
            Bounds = default,
            Policy = RegionPolicy.Absolute,
        };

        Assert.Equal(name, region.Name);
    }

    [Fact]
    public void Record_equality_compares_all_components()
    {
        var bounds = new Rect { X = 0, Y = 0, Width = 10, Height = 10 };
        var a = new DialogRegion { Name = "X", Bounds = bounds, Policy = RegionPolicy.Absolute };
        var b = new DialogRegion { Name = "X", Bounds = bounds, Policy = RegionPolicy.Absolute };
        var c = new DialogRegion { Name = "X", Bounds = bounds, Policy = RegionPolicy.RightPacked };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
