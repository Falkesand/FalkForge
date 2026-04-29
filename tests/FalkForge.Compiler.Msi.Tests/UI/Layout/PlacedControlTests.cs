using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class PlacedControlTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var control = new PlacedControl
        {
            Name = "Next",
            Type = "PushButton",
        };

        Assert.Equal("Next", control.Name);
        Assert.Equal("PushButton", control.Type);
        Assert.Equal(string.Empty, control.TextOrLocKey);
        Assert.Null(control.Property);
        Assert.Null(control.OverrideX);
        Assert.Null(control.OverrideY);
        Assert.Null(control.OverrideWidth);
        Assert.Null(control.OverrideHeight);
    }

    [Fact]
    public void Construct_with_invalid_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new PlacedControl
        {
            Name = "1Bad",
            Type = "Text",
        });
    }

    [Fact]
    public void Construct_with_empty_type_throws()
    {
        Assert.Throws<ArgumentException>(() => new PlacedControl
        {
            Name = "Foo",
            Type = "   ",
        });
    }

    [Fact]
    public void With_expression_replaces_property()
    {
        var control = new PlacedControl
        {
            Name = "Path",
            Type = "Edit",
            TextOrLocKey = "!(loc.PathLabel)",
            Property = "INSTALLDIR",
        };

        var copy = control with { Property = "TARGETDIR" };

        Assert.Equal("TARGETDIR", copy.Property);
        Assert.Equal("INSTALLDIR", control.Property);
        Assert.Equal("Path", copy.Name);
        Assert.Equal("!(loc.PathLabel)", copy.TextOrLocKey);
    }

    [Fact]
    public void Override_coordinates_round_trip()
    {
        var control = new PlacedControl
        {
            Name = "Logo",
            Type = "Bitmap",
            OverrideX = 10,
            OverrideY = 20,
            OverrideWidth = 100,
            OverrideHeight = 50,
        };

        Assert.Equal(10, control.OverrideX);
        Assert.Equal(20, control.OverrideY);
        Assert.Equal(100, control.OverrideWidth);
        Assert.Equal(50, control.OverrideHeight);
    }
}
