using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogContentTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            Placements = ImmutableArray<RegionPlacement>.Empty,
        };

        Assert.Equal("WelcomeDlg", content.Name);
        Assert.Equal("Welcome", content.Kind);
        Assert.True(content.Placements.IsEmpty);
        Assert.Null(content.FirstControl);
        Assert.Null(content.DefaultControl);
        Assert.Null(content.CancelControl);
        Assert.Null(content.TitleLocKey);
    }

    [Fact]
    public void Construct_with_invalid_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogContent
        {
            Name = "1Bad",
            Kind = "Welcome",
            Placements = ImmutableArray<RegionPlacement>.Empty,
        });
    }

    [Fact]
    public void Construct_with_invalid_kind_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = " ",
            Placements = ImmutableArray<RegionPlacement>.Empty,
        });
    }

    [Fact]
    public void With_expression_replaces_placements()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            Placements = ImmutableArray<RegionPlacement>.Empty,
            TitleLocKey = "!(loc.WelcomeTitle)",
        };

        var placement = new RegionPlacement { RegionName = "ButtonRow" };
        var updated = content with { Placements = ImmutableArray.Create(placement) };

        Assert.Single(updated.Placements);
        Assert.True(content.Placements.IsEmpty);
        Assert.Equal("!(loc.WelcomeTitle)", updated.TitleLocKey);
    }
}
