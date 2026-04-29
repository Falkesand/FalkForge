using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class WelcomeDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        var content = WelcomeDlgBuilder.Build();

        Assert.Equal("WelcomeDlg", content.Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        var content = WelcomeDlgBuilder.Build();

        Assert.Equal("Welcome", content.Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // Legacy WelcomeDlg has Title (TitleRow), Description (ContentArea), BottomLine, [Next, Cancel] (ButtonRow).
        // Four populated regions.
        var content = WelcomeDlgBuilder.Build();

        Assert.Equal(4, content.Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_expected_buttons()
    {
        var content = WelcomeDlgBuilder.Build();

        var buttonRow = content.Placements.Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        Assert.Equal(new[] { "Next", "Cancel" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        var content = WelcomeDlgBuilder.Build();

        Assert.Equal("Next", content.FirstControl);
    }

    [Fact]
    public void Build_default_and_cancel_controls_match_legacy()
    {
        var content = WelcomeDlgBuilder.Build();

        Assert.Equal("Next", content.DefaultControl);
        Assert.Equal("Cancel", content.CancelControl);
    }

    [Fact]
    public void Compose_via_standard_layout_produces_model_with_legacy_title_geometry()
    {
        var content = WelcomeDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var title = model.Controls.Single(c => c.Name == "Title");

        Assert.Equal(MsiControlType.Text, title.Type);
        Assert.Equal(15, title.X);
        Assert.Equal(6, title.Y);
        Assert.Equal(200, title.Width);
        Assert.Equal(15, title.Height);
    }

    [Fact]
    public void Compose_description_geometry_matches_legacy()
    {
        var content = WelcomeDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var description = model.Controls.Single(c => c.Name == "Description");

        Assert.Equal(MsiControlType.Text, description.Type);
        Assert.Equal(25, description.X);
        Assert.Equal(23, description.Y);
        Assert.Equal(280, description.Width);
        Assert.Equal(40, description.Height);
    }

    [Fact]
    public void Compose_bottom_line_geometry_matches_legacy()
    {
        var content = WelcomeDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var line = model.Controls.Single(c => c.Name == "BottomLine");

        Assert.Equal(MsiControlType.Line, line.Type);
        Assert.Equal(0, line.X);
        Assert.Equal(234, line.Y);
        Assert.Equal(370, line.Width);
        Assert.Equal(0, line.Height);
    }

    [Fact]
    public void Compose_cancel_button_geometry_uses_layout_right_packed_position()
    {
        // ButtonRow uses RightPacked with default Gap=8; legacy Cancel was at X=304 which matches the
        // right edge minus button width. Cancel is the rightmost button so it lands at X=304 exactly.
        var content = WelcomeDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(MsiControlType.PushButton, cancel.Type);
        Assert.Equal(304, cancel.X);
        Assert.Equal(243, cancel.Y);
        Assert.Equal(56, cancel.Width);
        Assert.Equal(17, cancel.Height);
    }
}
