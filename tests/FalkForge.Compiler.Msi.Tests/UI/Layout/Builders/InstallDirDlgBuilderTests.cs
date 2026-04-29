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
}
