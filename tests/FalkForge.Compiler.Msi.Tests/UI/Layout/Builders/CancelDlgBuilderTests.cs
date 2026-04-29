using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class CancelDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("CancelDlg", CancelDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("Cancel", CancelDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // Legacy CancelDlg has Text (ContentArea) + Yes/No (ButtonRow). The declarative builder
        // also includes a Title placement so the standard layout still has a focal heading and
        // a BottomLine separator is omitted because the legacy modal does not draw one.
        // ContentArea + ButtonRow = 2 placements.
        Assert.Equal(2, CancelDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_yes_and_no()
    {
        var buttonRow = CancelDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        // RightPacked declarative order: rightmost-first. Legacy No is right of Yes.
        Assert.Equal(new[] { "No", "Yes" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("No", CancelDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Build_default_and_cancel_controls_match_legacy()
    {
        var content = CancelDlgBuilder.Build();

        Assert.Equal("No", content.DefaultControl);
        Assert.Equal("No", content.CancelControl);
    }

    [Fact]
    public void Compose_includes_text_and_two_buttons()
    {
        // CancelDlg deviates structurally: legacy is a 260x85 modal but the builder targets the
        // standard 370x270 canvas because per-template layouts arrive in phase 7+. Assert
        // structural equivalence (one Text, Yes + No PushButtons) rather than byte-identical
        // geometry.
        var model = DialogComposer.Compose(CancelDlgBuilder.Build(), Layouts.Standard370x270);

        Assert.Contains(model.Controls, c => c.Name == "Text" && c.Type == MsiControlType.Text);
        Assert.Contains(model.Controls, c => c.Name == "Yes" && c.Type == MsiControlType.PushButton);
        Assert.Contains(model.Controls, c => c.Name == "No" && c.Type == MsiControlType.PushButton);
    }
}
