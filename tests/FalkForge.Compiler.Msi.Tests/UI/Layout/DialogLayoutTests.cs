using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogLayoutTests
{
    private static DialogRegion Region(string name, Rect? bounds = null, RegionPolicy policy = RegionPolicy.Absolute)
    {
        return new DialogRegion
        {
            Name = name,
            Bounds = bounds ?? new Rect { X = 0, Y = 0, Width = 10, Height = 10 },
            Policy = policy,
        };
    }

    [Fact]
    public void Construct_with_one_region_succeeds()
    {
        var layout = new DialogLayout
        {
            Name = "Welcome",
            Regions = ImmutableArray.Create(Region("Body")),
        };

        Assert.Equal("Welcome", layout.Name);
        Assert.Equal(370, layout.CanvasWidth);
        Assert.Equal(270, layout.CanvasHeight);
        Assert.Single(layout.Regions);
    }

    [Fact]
    public void Construct_with_zero_regions_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogLayout
        {
            Name = "Empty",
            Regions = ImmutableArray<DialogRegion>.Empty,
        });
    }

    [Fact]
    public void Construct_with_default_regions_array_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogLayout
        {
            Name = "Default",
            Regions = default,
        });
    }

    [Fact]
    public void Construct_with_duplicate_region_names_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogLayout
        {
            Name = "Dup",
            Regions = ImmutableArray.Create(Region("Body"), Region("Body")),
        });
    }

    [Theory]
    [InlineData(0, 270)]
    [InlineData(-1, 270)]
    [InlineData(370, 0)]
    [InlineData(370, -10)]
    public void Construct_with_invalid_canvas_throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DialogLayout
        {
            Name = "Bad",
            CanvasWidth = width,
            CanvasHeight = height,
            Regions = ImmutableArray.Create(Region("Body")),
        });
    }

    [Fact]
    public void Construct_with_invalid_layout_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogLayout
        {
            Name = "1bad-name",
            Regions = ImmutableArray.Create(Region("Body")),
        });
    }

    [Fact]
    public void TryGetRegion_returns_true_for_known_name()
    {
        var layout = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(Region("Body"), Region("Buttons")),
        };

        var found = layout.TryGetRegion("Buttons", out var region);

        Assert.True(found);
        Assert.Equal("Buttons", region.Name);
    }

    [Fact]
    public void TryGetRegion_returns_false_for_unknown_name()
    {
        var layout = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(Region("Body")),
        };

        var found = layout.TryGetRegion("NotThere", out var region);

        Assert.False(found);
        Assert.Equal(default, region);
    }

    [Fact]
    public void TryGetRegion_is_case_sensitive()
    {
        var layout = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(Region("Body")),
        };

        Assert.False(layout.TryGetRegion("body", out _));
        Assert.True(layout.TryGetRegion("Body", out _));
    }

    [Fact]
    public void With_replaces_named_region_returning_new_layout()
    {
        var original = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(
                Region("Body", new Rect { X = 0, Y = 0, Width = 10, Height = 10 }),
                Region("Buttons", new Rect { X = 0, Y = 0, Width = 50, Height = 20 })),
        };
        var replacement = new DialogRegion
        {
            Name = "Buttons",
            Bounds = new Rect { X = 0, Y = 0, Width = 99, Height = 99 },
            Policy = RegionPolicy.RightPacked,
        };

        var modified = original.With("Buttons", replacement);

        Assert.NotSame(original, modified);
        Assert.True(modified.TryGetRegion("Buttons", out var found));
        Assert.Equal(99, found.Bounds.Width);
        Assert.Equal(RegionPolicy.RightPacked, found.Policy);
        // Original unchanged.
        Assert.True(original.TryGetRegion("Buttons", out var orig));
        Assert.Equal(50, orig.Bounds.Width);
    }

    [Fact]
    public void With_unknown_region_throws()
    {
        var layout = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(Region("Body")),
        };
        var replacement = Region("Other");

        Assert.Throws<ArgumentException>(() => layout.With("Other", replacement));
    }

    [Fact]
    public void With_replacement_is_used_under_original_key()
    {
        var layout = new DialogLayout
        {
            Name = "L",
            Regions = ImmutableArray.Create(Region("Body")),
        };
        // Replacement may carry a different name; the slot keyed by 'Body' is what matters.
        var replacement = new DialogRegion
        {
            Name = "Body",
            Bounds = new Rect { X = 5, Y = 5, Width = 5, Height = 5 },
            Policy = RegionPolicy.SingleControl,
        };

        var modified = layout.With("Body", replacement);

        Assert.True(modified.TryGetRegion("Body", out var region));
        Assert.Equal(RegionPolicy.SingleControl, region.Policy);
    }
}
