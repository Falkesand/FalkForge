using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class LicenseDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        var content = LicenseDlgBuilder.Build();

        Assert.Equal("LicenseAgreementDlg", content.Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        var content = LicenseDlgBuilder.Build();

        Assert.Equal("License", content.Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        // Title (TitleRow), [LicenseText, LicenseAccepted] (ContentArea), BottomLine, [Cancel, Next, Back] (ButtonRow).
        var content = LicenseDlgBuilder.Build();

        Assert.Equal(4, content.Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_expected_buttons_in_right_packed_order()
    {
        var content = LicenseDlgBuilder.Build();

        var buttonRow = content.Placements.Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        // RightPacked: rightmost-first declarative order. Visual order is Back, Next, Cancel.
        Assert.Equal(new[] { "Cancel", "Next", "Back" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        var content = LicenseDlgBuilder.Build();

        Assert.Equal("LicenseText", content.FirstControl);
    }

    [Fact]
    public void Build_default_and_cancel_controls_match_legacy()
    {
        var content = LicenseDlgBuilder.Build();

        Assert.Equal("Next", content.DefaultControl);
        Assert.Equal("Cancel", content.CancelControl);
    }

    [Fact]
    public void Compose_license_text_geometry_matches_legacy()
    {
        var content = LicenseDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var licenseText = model.Controls.Single(c => c.Name == "LicenseText");

        Assert.Equal(MsiControlType.ScrollableText, licenseText.Type);
        Assert.Equal(20, licenseText.X);
        Assert.Equal(60, licenseText.Y);
        Assert.Equal(330, licenseText.Width);
        Assert.Equal(140, licenseText.Height);
        Assert.Equal("LicenseText", licenseText.Property);
    }

    [Fact]
    public void Compose_license_accepted_checkbox_geometry_matches_legacy()
    {
        var content = LicenseDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var checkbox = model.Controls.Single(c => c.Name == "LicenseAccepted");

        Assert.Equal(MsiControlType.CheckBox, checkbox.Type);
        Assert.Equal(20, checkbox.X);
        Assert.Equal(207, checkbox.Y);
        Assert.Equal(330, checkbox.Width);
        Assert.Equal(18, checkbox.Height);
        Assert.Equal("LicenseAccepted", checkbox.Property);
    }

    [Fact]
    public void Compose_cancel_button_lands_at_legacy_x_coordinate()
    {
        var content = LicenseDlgBuilder.Build();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(304, cancel.X);
        Assert.Equal(243, cancel.Y);
    }
}
