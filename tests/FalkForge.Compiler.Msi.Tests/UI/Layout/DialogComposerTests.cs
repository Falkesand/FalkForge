using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogComposerTests
{
    private static DialogContent MinimalContent(string name = "WelcomeDlg") => new()
    {
        Name = name,
        Kind = "Welcome",
        Placements = ImmutableArray<RegionPlacement>.Empty,
    };

    private static PlacedControl Button(string name) =>
        new() { Name = name, Type = "PushButton" };

    [Fact]
    public void Compose_with_null_content_throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DialogComposer.Compose(null!, Layouts.Standard370x270));
    }

    [Fact]
    public void Compose_with_null_layout_throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DialogComposer.Compose(MinimalContent(), null!));
    }

    [Fact]
    public void Compose_with_minimal_content_returns_model_with_matching_name()
    {
        var content = MinimalContent("MyDialog");

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("MyDialog", model.Name);
    }

    [Fact]
    public void Compose_with_minimal_content_returns_empty_controls()
    {
        var model = DialogComposer.Compose(MinimalContent(), Layouts.Standard370x270);

        Assert.Empty(model.Controls);
    }

    [Fact]
    public void Compose_uses_layout_canvas_dimensions()
    {
        var model = DialogComposer.Compose(MinimalContent(), Layouts.Standard370x270);

        Assert.Equal(370, model.Width);
        Assert.Equal(270, model.Height);
    }

    [Fact]
    public void Compose_with_button_row_emits_three_controls_at_correct_x()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            FirstControl = "Cancel",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("Cancel"), Button("Next"), Button("Back")),
                }),
        };

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal(3, model.Controls.Count);
        Assert.Equal("Cancel", model.Controls[0].Name);
        Assert.Equal(MsiControlType.PushButton, model.Controls[0].Type);
        Assert.Equal(304, model.Controls[0].X);
        Assert.Equal(243, model.Controls[0].Y);
        Assert.Equal(56, model.Controls[0].Width);
        Assert.Equal(17, model.Controls[0].Height);

        Assert.Equal("Next", model.Controls[1].Name);
        Assert.Equal(240, model.Controls[1].X);

        Assert.Equal("Back", model.Controls[2].Name);
        Assert.Equal(176, model.Controls[2].X);
    }

    [Fact]
    public void Compose_with_unknown_control_type_throws()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ContentArea",
                    Controls = ImmutableArray.Create(
                        new PlacedControl { Name = "Bogus", Type = "BogusType" }),
                }),
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DialogComposer.Compose(content, Layouts.Standard370x270));
        Assert.Contains("BogusType", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_first_control_falls_back_to_first_pushbutton_in_buttonrow()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            // FirstControl not set -> fall back to first PushButton in ButtonRow
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "ButtonRow",
                    Controls = ImmutableArray.Create(Button("Cancel"), Button("Next"), Button("Back")),
                }),
        };

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("Cancel", model.FirstControl);
    }

    [Fact]
    public void Compose_with_unknown_region_name_throws()
    {
        var content = new DialogContent
        {
            Name = "WelcomeDlg",
            Kind = "Welcome",
            Placements = ImmutableArray.Create(
                new RegionPlacement
                {
                    RegionName = "NoSuchRegion",
                    Controls = ImmutableArray.Create(Button("X")),
                }),
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => DialogComposer.Compose(content, Layouts.Standard370x270));
        Assert.Contains("NoSuchRegion", ex.Message, StringComparison.Ordinal);
    }
}
