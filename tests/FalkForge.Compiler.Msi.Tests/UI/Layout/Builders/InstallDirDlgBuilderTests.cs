using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class InstallDirDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("InstallDirDlg", InstallDirDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("InstallDir", InstallDirDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // TitleRow, ContentArea (Description, FolderLabel, Folder, ChangeFolder), BottomLine, ButtonRow.
        Assert.Equal(4, InstallDirDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_expected_buttons()
    {
        var buttonRow = InstallDirDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        Assert.Equal(new[] { "Cancel", "Next", "Back" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("Folder", InstallDirDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Compose_folder_pathedit_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(InstallDirDlgBuilder.Build(), Layouts.Standard370x270);
        var folder = model.Controls.Single(c => c.Name == "Folder");

        Assert.Equal(MsiControlType.PathEdit, folder.Type);
        Assert.Equal(20, folder.X);
        Assert.Equal(80, folder.Y);
        Assert.Equal(260, folder.Width);
        Assert.Equal(18, folder.Height);
        Assert.Equal("INSTALLDIR", folder.Property);
    }

    [Fact]
    public void Compose_change_folder_button_geometry_matches_legacy()
    {
        var model = DialogComposer.Compose(InstallDirDlgBuilder.Build(), Layouts.Standard370x270);
        var change = model.Controls.Single(c => c.Name == "ChangeFolder");

        Assert.Equal(MsiControlType.PushButton, change.Type);
        Assert.Equal(284, change.X);
        Assert.Equal(80, change.Y);
        Assert.Equal(56, change.Width);
        Assert.Equal(17, change.Height);
    }

    [Fact]
    public void Compose_cancel_button_lands_at_legacy_x_coordinate()
    {
        var model = DialogComposer.Compose(InstallDirDlgBuilder.Build(), Layouts.Standard370x270);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(304, cancel.X);
    }

    [Fact]
    public void Build_emits_events_for_each_button()
    {
        // Legacy BuildInstallDirDlg emits six ControlEvent rows: ChangeFolder x3 (SetProperty,
        // SpawnDialog, SetProperty), Back NewDialog, Next EndDialog, Cancel SpawnDialog.
        var ctx = new DialogFlowContext { BackDialog = "WelcomeDlg" };

        var content = InstallDirDlgBuilder.Build(ctx);

        Assert.Equal(6, content.Events.Length);
    }

    [Fact]
    public void Build_event_targets_match_flow_context()
    {
        var ctx = new DialogFlowContext { BackDialog = "BackTarget", CancelDialog = "MyCancelDlg" };

        var content = InstallDirDlgBuilder.Build(ctx);

        var back = content.Events.Single(e => e.Control == "Back");
        Assert.Equal("NewDialog", back.Event);
        Assert.Equal("BackTarget", back.Argument);

        var cancel = content.Events.Single(e => e.Control == "Cancel");
        Assert.Equal("SpawnDialog", cancel.Event);
        Assert.Equal("MyCancelDlg", cancel.Argument);

        var next = content.Events.Single(e => e.Control == "Next");
        Assert.Equal("EndDialog", next.Event);
        Assert.Equal("Return", next.Argument);
    }

    [Fact]
    public void Build_change_folder_emits_three_ordered_events()
    {
        // Legacy ChangeFolder sequence: SetProperty[_BrowseProperty]=[INSTALLDIR],
        // SpawnDialog BrowseDlg, SetProperty[INSTALLDIR]=[_BrowseProperty].
        var content = InstallDirDlgBuilder.Build(new DialogFlowContext { BackDialog = "B" });

        var changeFolderEvents = content.Events.Where(e => e.Control == "ChangeFolder")
            .OrderBy(e => e.Order).ToArray();
        Assert.Equal(3, changeFolderEvents.Length);

        Assert.Equal("[_BrowseProperty]", changeFolderEvents[0].Event);
        Assert.Equal("[INSTALLDIR]", changeFolderEvents[0].Argument);

        Assert.Equal("SpawnDialog", changeFolderEvents[1].Event);
        Assert.Equal("BrowseDlg", changeFolderEvents[1].Argument);

        Assert.Equal("[INSTALLDIR]", changeFolderEvents[2].Event);
        Assert.Equal("[_BrowseProperty]", changeFolderEvents[2].Argument);
    }
}
