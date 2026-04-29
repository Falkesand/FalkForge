using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class InstallScopeDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        var content = InstallScopeDlgBuilder.Build();

        Assert.Equal("InstallScopeDlg", content.Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        var content = InstallScopeDlgBuilder.Build();

        Assert.Equal("InstallScope", content.Kind);
    }

    [Fact]
    public void Build_first_default_cancel_controls_match_legacy()
    {
        var content = InstallScopeDlgBuilder.Build();

        Assert.Equal("PerMachine", content.FirstControl);
        Assert.Equal("PerMachine", content.DefaultControl);
        Assert.Equal("Cancel", content.CancelControl);
    }

    [Fact]
    public void Build_button_row_has_back_and_cancel()
    {
        var content = InstallScopeDlgBuilder.Build();

        var buttonRow = content.Placements.Single(p => p.RegionName == "ButtonRow");
        var names = buttonRow.Controls.Select(c => c.Name).ToArray();

        // RightPacked policy lays out controls right-to-left, so the declarative order is
        // rightmost-first: Cancel precedes Back.
        Assert.Equal(new[] { "Cancel", "Back" }, names);
    }

    [Fact]
    public void Build_content_area_includes_per_machine_and_per_user_buttons()
    {
        var content = InstallScopeDlgBuilder.Build();

        var contentArea = content.Placements.Single(p => p.RegionName == "ContentArea");
        var names = contentArea.Controls.Select(c => c.Name).ToArray();

        Assert.Contains("PerMachine", names);
        Assert.Contains("PerUser", names);
        Assert.Contains("PerMachineDesc", names);
        Assert.Contains("PerUserDesc", names);
    }

    [Fact]
    public void Build_emits_six_events_matching_legacy()
    {
        // Legacy BuildInstallScopeDlg emits exactly 6 ControlEvent rows:
        //   Back NewDialog -> Welcome
        //   PerMachine [ALLUSERS] = "1" (Order 1)
        //   PerMachine NewDialog -> LicenseAgreement (Order 2)
        //   PerUser    [ALLUSERS] = "{}" (Order 1)
        //   PerUser    NewDialog -> LicenseAgreement (Order 2)
        //   Cancel SpawnDialog -> Cancel
        var ctx = new DialogFlowContext
        {
            BackDialog = "WelcomeDlg",
            NextDialog = "LicenseAgreementDlg",
        };

        var content = InstallScopeDlgBuilder.Build(ctx);

        Assert.Equal(6, content.Events.Length);
    }

    [Fact]
    public void Build_back_event_targets_flow_back_dialog()
    {
        var ctx = new DialogFlowContext
        {
            BackDialog = "MyBack",
            NextDialog = "MyNext",
        };

        var content = InstallScopeDlgBuilder.Build(ctx);
        var back = content.Events.Single(e => e.Control == "Back");

        Assert.Equal("NewDialog", back.Event);
        Assert.Equal("MyBack", back.Argument);
    }

    [Fact]
    public void Build_per_machine_sets_allusers_to_one()
    {
        var ctx = new DialogFlowContext { NextDialog = "LicenseAgreementDlg" };
        var content = InstallScopeDlgBuilder.Build(ctx);

        var setProp = content.Events.Single(e => e.Control == "PerMachine" && e.Event == "[ALLUSERS]");

        Assert.Equal("1", setProp.Argument);
        Assert.Equal(1, setProp.Order);
    }

    [Fact]
    public void Build_per_user_sets_allusers_to_empty_brace()
    {
        var ctx = new DialogFlowContext { NextDialog = "LicenseAgreementDlg" };
        var content = InstallScopeDlgBuilder.Build(ctx);

        var setProp = content.Events.Single(e => e.Control == "PerUser" && e.Event == "[ALLUSERS]");

        // Legacy uses "{}" to clear the ALLUSERS property (per-user install).
        Assert.Equal("{}", setProp.Argument);
        Assert.Equal(1, setProp.Order);
    }

    [Fact]
    public void Build_per_machine_advances_to_next_dialog_with_order_two()
    {
        var ctx = new DialogFlowContext { NextDialog = "LicenseAgreementDlg" };
        var content = InstallScopeDlgBuilder.Build(ctx);

        var advance = content.Events.Single(e =>
            e.Control == "PerMachine" && e.Event == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", advance.Argument);
        Assert.Equal(2, advance.Order);
    }

    [Fact]
    public void Build_per_user_advances_to_next_dialog_with_order_two()
    {
        var ctx = new DialogFlowContext { NextDialog = "LicenseAgreementDlg" };
        var content = InstallScopeDlgBuilder.Build(ctx);

        var advance = content.Events.Single(e =>
            e.Control == "PerUser" && e.Event == "NewDialog");

        Assert.Equal("LicenseAgreementDlg", advance.Argument);
        Assert.Equal(2, advance.Order);
    }

    [Fact]
    public void Build_cancel_spawns_cancel_dialog()
    {
        var content = InstallScopeDlgBuilder.Build(new DialogFlowContext
        {
            BackDialog = "WelcomeDlg",
            NextDialog = "LicenseAgreementDlg",
            CancelDialog = "MyCancelDlg",
        });

        var cancel = content.Events.Single(e => e.Control == "Cancel");

        Assert.Equal("SpawnDialog", cancel.Event);
        Assert.Equal("MyCancelDlg", cancel.Argument);
    }

    [Fact]
    public void Compose_via_standard_layout_places_per_machine_button()
    {
        var ctx = new DialogFlowContext
        {
            BackDialog = "WelcomeDlg",
            NextDialog = "LicenseAgreementDlg",
        };

        var content = InstallScopeDlgBuilder.Build(ctx);
        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        var perMachine = model.Controls.Single(c => c.Name == "PerMachine");

        Assert.Equal(MsiControlType.PushButton, perMachine.Type);
    }

    [Fact]
    public void Compose_emits_six_msi_control_events()
    {
        var ctx = new DialogFlowContext
        {
            BackDialog = "WelcomeDlg",
            NextDialog = "LicenseAgreementDlg",
        };

        var content = InstallScopeDlgBuilder.Build(ctx);
        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal(6, model.Events.Count);
    }
}
