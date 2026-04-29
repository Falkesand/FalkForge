using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class ExitDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("ExitDlg", ExitDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("Exit", ExitDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // TitleRow, ContentArea (Description), BottomLine, ButtonRow (Finish).
        Assert.Equal(4, ExitDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_only_finish()
    {
        var buttonRow = ExitDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        Assert.Equal(new[] { "Finish" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("Finish", ExitDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Build_cancel_control_matches_legacy()
    {
        // Legacy CancelControl = "Finish" so Esc completes the dialog rather than spawning Cancel.
        Assert.Equal("Finish", ExitDlgBuilder.Build().CancelControl);
    }

    [Fact]
    public void Compose_finish_button_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(ExitDlgBuilder.Build(), Layouts.Standard370x270);
        var finish = model.Controls.Single(c => c.Name == "Finish");

        Assert.Equal(MsiControlType.PushButton, finish.Type);
        Assert.Equal(304, finish.X);
        Assert.Equal(243, finish.Y);
        Assert.Equal(56, finish.Width);
        Assert.Equal(17, finish.Height);
    }

    [Fact]
    public void Compose_description_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(ExitDlgBuilder.Build(), Layouts.Standard370x270);
        var description = model.Controls.Single(c => c.Name == "Description");

        Assert.Equal(25, description.X);
        Assert.Equal(23, description.Y);
        Assert.Equal(280, description.Width);
        Assert.Equal(20, description.Height);
    }
}
