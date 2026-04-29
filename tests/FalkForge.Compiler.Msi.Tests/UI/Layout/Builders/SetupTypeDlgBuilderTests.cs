using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class SetupTypeDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("SetupTypeDlg", SetupTypeDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("SetupType", SetupTypeDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_placement_count_matches_legacy()
    {
        Assert.Equal(4, SetupTypeDlgBuilder.Build().Placements.Length);
    }

    [Fact]
    public void Build_button_row_has_expected_buttons()
    {
        var buttonRow = SetupTypeDlgBuilder.Build().Placements
            .Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        // Legacy SetupType has only Back + Cancel in the bottom row (no Next; choice is the
        // setup-type buttons in ContentArea).
        Assert.Equal(new[] { "Cancel", "Back" }, names);
    }

    [Fact]
    public void Build_first_control_matches_legacy()
    {
        Assert.Equal("TypicalButton", SetupTypeDlgBuilder.Build().FirstControl);
    }

    [Fact]
    public void Compose_setup_type_buttons_present_with_legacy_geometry()
    {
        var model = DialogComposer.Compose(SetupTypeDlgBuilder.Build(), Layouts.Standard370x270);

        var typical = model.Controls.Single(c => c.Name == "TypicalButton");
        Assert.Equal(MsiControlType.PushButton, typical.Type);
        Assert.Equal(40, typical.X);
        Assert.Equal(65, typical.Y);
        Assert.Equal(290, typical.Width);
        Assert.Equal(17, typical.Height);

        var custom = model.Controls.Single(c => c.Name == "CustomButton");
        Assert.Equal(40, custom.X);
        Assert.Equal(115, custom.Y);

        var complete = model.Controls.Single(c => c.Name == "CompleteButton");
        Assert.Equal(40, complete.X);
        Assert.Equal(165, complete.Y);
    }

    [Fact]
    public void Compose_cancel_button_lands_at_legacy_x_coordinate()
    {
        var model = DialogComposer.Compose(SetupTypeDlgBuilder.Build(), Layouts.Standard370x270);
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(304, cancel.X);
    }

    [Fact]
    public void Compose_includes_three_description_text_blocks()
    {
        var model = DialogComposer.Compose(SetupTypeDlgBuilder.Build(), Layouts.Standard370x270);

        Assert.Contains(model.Controls, c => c.Name == "TypicalDesc");
        Assert.Contains(model.Controls, c => c.Name == "CustomDesc");
        Assert.Contains(model.Controls, c => c.Name == "CompleteDesc");
    }

    [Fact]
    public void Build_emits_events_for_each_button()
    {
        // Legacy BuildSetupTypeDlg emits five ControlEvent rows: Back NewDialog,
        // TypicalButton, CustomButton, CompleteButton (each NewDialog), Cancel SpawnDialog.
        var content = SetupTypeDlgBuilder.Build(new DialogFlowContext { BackDialog = "LicenseAgreementDlg" });

        Assert.Equal(5, content.Events.Length);
    }

    [Fact]
    public void Build_event_targets_match_flow_context_and_legacy_routes()
    {
        var ctx = new DialogFlowContext
        {
            BackDialog = "LicenseAgreementDlg",
            CancelDialog = "MyCancelDlg",
        };

        var content = SetupTypeDlgBuilder.Build(ctx);

        var back = content.Events.Single(e => e.Control == "Back");
        Assert.Equal("NewDialog", back.Event);
        Assert.Equal("LicenseAgreementDlg", back.Argument);

        var typical = content.Events.Single(e => e.Control == "TypicalButton");
        Assert.Equal("NewDialog", typical.Event);
        Assert.Equal("ProgressDlg", typical.Argument);

        var custom = content.Events.Single(e => e.Control == "CustomButton");
        Assert.Equal("NewDialog", custom.Event);
        Assert.Equal("CustomizeDlg", custom.Argument);

        var complete = content.Events.Single(e => e.Control == "CompleteButton");
        Assert.Equal("NewDialog", complete.Event);
        Assert.Equal("ProgressDlg", complete.Argument);

        var cancel = content.Events.Single(e => e.Control == "Cancel");
        Assert.Equal("SpawnDialog", cancel.Event);
        Assert.Equal("MyCancelDlg", cancel.Argument);
    }
}
