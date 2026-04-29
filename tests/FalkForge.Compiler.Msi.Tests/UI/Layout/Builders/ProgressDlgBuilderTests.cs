using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class ProgressDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("ProgressDlg", ProgressDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("Progress", ProgressDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // TitleRow, ContentArea (StatusLabel, ActionText, ProgressBar), BottomLine, ButtonRow.
        Assert.Equal(4, ProgressDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_only_cancel()
    {
        var buttonRow = ProgressDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        Assert.Equal(new[] { "Cancel" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("Cancel", ProgressDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Compose_progress_bar_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(ProgressDlgBuilder.Build(), Layouts.Standard370x270);
        var bar = model.Controls.Single(c => c.Name == "ProgressBar");

        Assert.Equal(MsiControlType.ProgressBar, bar.Type);
        Assert.Equal(25, bar.X);
        Assert.Equal(70, bar.Y);
        Assert.Equal(320, bar.Width);
        Assert.Equal(10, bar.Height);
    }

    [Fact]
    public void Compose_status_label_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(ProgressDlgBuilder.Build(), Layouts.Standard370x270);
        var label = model.Controls.Single(c => c.Name == "StatusLabel");

        Assert.Equal(MsiControlType.Text, label.Type);
        Assert.Equal(25, label.X);
        Assert.Equal(55, label.Y);
        Assert.Equal(50, label.Width);
        Assert.Equal(10, label.Height);
    }

    [Fact]
    public void Compose_cancel_button_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(ProgressDlgBuilder.Build(), Layouts.Standard370x270);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(304, cancel.X);
        Assert.Equal(243, cancel.Y);
        Assert.Equal(56, cancel.Width);
        Assert.Equal(17, cancel.Height);
    }
}
