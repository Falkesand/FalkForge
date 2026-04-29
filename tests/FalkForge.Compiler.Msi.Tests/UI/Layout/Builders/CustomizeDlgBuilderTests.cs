using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class CustomizeDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("CustomizeDlg", CustomizeDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("Customize", CustomizeDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        Assert.Equal(4, CustomizeDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_expected_buttons()
    {
        var buttonRow = CustomizeDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        Assert.Equal(new[] { "Cancel", "Next", "Back" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("Tree", CustomizeDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Compose_selection_tree_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(CustomizeDlgBuilder.Build(), Layouts.Standard370x270);
        var tree = model.Controls.Single(c => c.Name == "Tree");

        Assert.Equal(MsiControlType.SelectionTree, tree.Type);
        Assert.Equal(25, tree.X);
        Assert.Equal(55, tree.Y);
        Assert.Equal(175, tree.Width);
        Assert.Equal(130, tree.Height);
        Assert.Equal("_BrowseProperty", tree.Property);
    }

    [Fact]
    public void Compose_volume_cost_list_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(CustomizeDlgBuilder.Build(), Layouts.Standard370x270);
        var diskCost = model.Controls.Single(c => c.Name == "DiskCost");

        Assert.Equal(MsiControlType.VolumeCostList, diskCost.Type);
        Assert.Equal(25, diskCost.X);
        Assert.Equal(195, diskCost.Y);
        Assert.Equal(320, diskCost.Width);
        Assert.Equal(30, diskCost.Height);
    }

    [Fact]
    public void Compose_includes_item_description_and_size_controls()
    {
        var model = DialogComposer.Compose(CustomizeDlgBuilder.Build(), Layouts.Standard370x270);

        Assert.Contains(model.Controls, c => c.Name == "ItemDescription");
        Assert.Contains(model.Controls, c => c.Name == "ItemSize");
    }

    [Fact]
    public void Build_emits_events_for_each_button()
    {
        // Legacy BuildCustomizeDlg emits three ControlEvent rows: Back NewDialog, Next NewDialog,
        // Cancel SpawnDialog.
        var ctx = new DialogFlowContext { BackDialog = "WelcomeDlg", NextDialog = "ProgressDlg" };

        var content = CustomizeDlgBuilder.Build(ctx);

        Assert.Equal(3, content.Events.Length);
    }

    [Fact]
    public void Build_event_targets_match_flow_context()
    {
        var ctx = new DialogFlowContext
        {
            BackDialog = "BackTarget",
            NextDialog = "NextTarget",
            CancelDialog = "MyCancelDlg",
        };

        var content = CustomizeDlgBuilder.Build(ctx);

        var back = content.Events.Single(e => e.Control == "Back");
        Assert.Equal("NewDialog", back.Event);
        Assert.Equal("BackTarget", back.Argument);

        var next = content.Events.Single(e => e.Control == "Next");
        Assert.Equal("NewDialog", next.Event);
        Assert.Equal("NextTarget", next.Argument);

        var cancel = content.Events.Single(e => e.Control == "Cancel");
        Assert.Equal("SpawnDialog", cancel.Event);
        Assert.Equal("MyCancelDlg", cancel.Argument);
    }
}
