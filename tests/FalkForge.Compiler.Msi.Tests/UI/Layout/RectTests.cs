using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class RectTests
{
    [Fact]
    public void Construct_with_valid_dimensions_preserves_values()
    {
        var rect = new Rect { X = 10, Y = 20, Width = 300, Height = 200 };

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(300, rect.Width);
        Assert.Equal(200, rect.Height);
    }

    [Fact]
    public void Construct_with_negative_width_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rect { X = 0, Y = 0, Width = -1, Height = 10 });
    }

    [Fact]
    public void Construct_with_negative_height_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Rect { X = 0, Y = 0, Width = 10, Height = -1 });
    }

    [Fact]
    public void Zero_width_or_height_is_allowed_for_spacer_rects()
    {
        var zeroWidth = new Rect { X = 5, Y = 5, Width = 0, Height = 10 };
        var zeroHeight = new Rect { X = 5, Y = 5, Width = 10, Height = 0 };
        var bothZero = new Rect { X = 0, Y = 0, Width = 0, Height = 0 };

        Assert.Equal(0, zeroWidth.Width);
        Assert.Equal(0, zeroHeight.Height);
        Assert.Equal(0, bothZero.Width);
        Assert.Equal(0, bothZero.Height);
    }

    [Fact]
    public void Negative_x_or_y_is_allowed()
    {
        var rect = new Rect { X = -5, Y = -10, Width = 10, Height = 10 };

        Assert.Equal(-5, rect.X);
        Assert.Equal(-10, rect.Y);
    }

    [Fact]
    public void Record_equality_compares_all_components()
    {
        var a = new Rect { X = 1, Y = 2, Width = 3, Height = 4 };
        var b = new Rect { X = 1, Y = 2, Width = 3, Height = 4 };
        var c = new Rect { X = 1, Y = 2, Width = 3, Height = 5 };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void With_expression_produces_modified_copy()
    {
        var original = new Rect { X = 1, Y = 2, Width = 3, Height = 4 };
        var modified = original with { Width = 99 };

        Assert.Equal(3, original.Width);
        Assert.Equal(99, modified.Width);
        Assert.Equal(1, modified.X);
    }
}
