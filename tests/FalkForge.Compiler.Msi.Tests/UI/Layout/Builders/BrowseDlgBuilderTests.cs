using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class BrowseDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("BrowseDlg", BrowseDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("Browse", BrowseDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // ContentArea (PathLabel, PathEdit, DirectoryList, Up, NewFolder) + ButtonRow (Cancel, OK).
        Assert.Equal(2, BrowseDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_ok_and_cancel()
    {
        var buttonRow = BrowseDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        // RightPacked declarative order: rightmost-first. Legacy Cancel is right of OK.
        Assert.Equal(new[] { "Cancel", "OK" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("DirectoryList", BrowseDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Build_default_and_cancel_controls_match_legacy()
    {
        var content = BrowseDlgBuilder.Build();

        Assert.Equal("OK", content.DefaultControl);
        Assert.Equal("Cancel", content.CancelControl);
    }

    [Fact]
    public void Compose_includes_directory_list_with_property_binding()
    {
        var model = DialogComposer.Compose(BrowseDlgBuilder.Build(), Layouts.Standard370x270);
        var list = model.Controls.Single(c => c.Name == "DirectoryList");

        Assert.Equal(MsiControlType.DirectoryList, list.Type);
        Assert.Equal("_BrowseProperty", list.Property);
    }

    [Fact]
    public void Compose_includes_path_edit_with_property_binding()
    {
        var model = DialogComposer.Compose(BrowseDlgBuilder.Build(), Layouts.Standard370x270);
        var pathEdit = model.Controls.Single(c => c.Name == "PathEdit");

        Assert.Equal(MsiControlType.PathEdit, pathEdit.Type);
        Assert.Equal("_BrowseProperty", pathEdit.Property);
    }

    [Fact]
    public void Compose_includes_up_and_new_folder_buttons()
    {
        var model = DialogComposer.Compose(BrowseDlgBuilder.Build(), Layouts.Standard370x270);

        Assert.Contains(model.Controls, c => c.Name == "Up" && c.Type == MsiControlType.PushButton);
        Assert.Contains(model.Controls, c => c.Name == "NewFolder" && c.Type == MsiControlType.PushButton);
    }
}
