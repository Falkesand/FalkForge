using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout.Builders;

public sealed class MsiRMFilesInUseDlgBuilderTests
{
    [Fact]
    public void Build_returns_dialog_content_with_expected_name()
    {
        Assert.Equal("MsiRMFilesInUse", MsiRMFilesInUseDlgBuilder.Build().Name);
    }

    [Fact]
    public void Build_dialog_kind_matches_expected()
    {
        Assert.Equal("RestartManager", MsiRMFilesInUseDlgBuilder.Build().Kind);
    }

    [Fact]
    public void Build_list_control_binds_to_FileInUseProcess_property()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();
        var contentArea = content.Placements.Single(p => p.RegionName == "ContentArea");
        var list = contentArea.Controls.Single(c => c.Name == "List");

        Assert.Equal("ListBox", list.Type);
        Assert.Equal("FileInUseProcess", list.Property);
    }

    [Fact]
    public void Build_shutdown_option_is_radio_button_group()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();
        var contentArea = content.Placements.Single(p => p.RegionName == "ContentArea");
        var shutdownOption = contentArea.Controls.Single(c => c.Name == "ShutdownOption");

        Assert.Equal("RadioButtonGroup", shutdownOption.Type);
        Assert.Equal(MsiRMFilesInUseDlgBuilder.OptionProperty, shutdownOption.Property);
    }

    [Fact]
    public void Build_declares_two_radio_buttons_with_documented_values()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();

        Assert.Equal(2, content.RadioButtons.Length);

        var useRm = content.RadioButtons.Single(r => r.Value == MsiRMFilesInUseDlgBuilder.UseRestartManagerValue);
        Assert.Equal(1, useRm.Order);
        Assert.Equal(MsiRMFilesInUseDlgBuilder.OptionProperty, useRm.Property);

        var dontUseRm = content.RadioButtons.Single(r => r.Value == MsiRMFilesInUseDlgBuilder.DoNotUseRestartManagerValue);
        Assert.Equal(2, dontUseRm.Order);
        Assert.Equal(MsiRMFilesInUseDlgBuilder.OptionProperty, dontUseRm.Property);
    }

    [Fact]
    public void Build_ok_publishes_rm_shutdown_and_restart_with_argument_zero()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();

        var rmEvent = content.Events.Single(e => e.Control == "OK" && e.Event == "RMShutdownAndRestart");
        Assert.Equal("0", rmEvent.Argument);
    }

    [Fact]
    public void Build_ok_rm_event_is_conditioned_on_the_use_rm_option()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();

        var rmEvent = content.Events.Single(e => e.Control == "OK" && e.Event == "RMShutdownAndRestart");
        Assert.Equal(
            $"{MsiRMFilesInUseDlgBuilder.OptionProperty}~=\"{MsiRMFilesInUseDlgBuilder.UseRestartManagerValue}\"",
            rmEvent.Condition);
    }

    [Fact]
    public void Build_ok_publishes_end_dialog_return_after_the_rm_event()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();

        var rmEvent = content.Events.Single(e => e.Control == "OK" && e.Event == "RMShutdownAndRestart");
        var endDialog = content.Events.Single(e => e.Control == "OK" && e.Event == "EndDialog");

        Assert.Equal("Return", endDialog.Argument);
        Assert.True(rmEvent.Order < endDialog.Order);
    }

    [Fact]
    public void Build_cancel_publishes_end_dialog_exit()
    {
        var content = MsiRMFilesInUseDlgBuilder.Build();

        var cancelEvent = content.Events.Single(e => e.Control == "Cancel");
        Assert.Equal("EndDialog", cancelEvent.Event);
        Assert.Equal("Exit", cancelEvent.Argument);
    }

    [Fact]
    public void Compose_places_ok_at_240_and_cancel_at_304()
    {
        var model = DialogComposer.Compose(MsiRMFilesInUseDlgBuilder.Build(), Layouts.Standard370x270);

        var ok = model.Controls.Single(c => c.Name == "OK");
        var cancel = model.Controls.Single(c => c.Name == "Cancel");

        Assert.Equal(240, ok.X);
        Assert.Equal(304, cancel.X);
    }

    [Fact]
    public void Compose_dialog_attributes_equal_visible_modal_minimize_trackdiskspace_keepmodeless()
    {
        var model = DialogComposer.Compose(MsiRMFilesInUseDlgBuilder.Build(), Layouts.Standard370x270);

        // 0x37 = Visible (0x01) | Modal (0x02) | Minimize (0x04) | TrackDiskSpace (0x20) |
        // KeepModeless (0x10). Asserting the whole value (not just the KeepModeless bit) pins
        // every component: masking to `& 0x10` alone would let the other four bits mutate freely
        // (e.g. dropping Modal/Visible, which would make the dialog invisible/non-modal) without
        // failing this test.
        Assert.Equal(0x37, (int)model.Attributes);
    }

    // Regression test for the keyboard-consent defect: before DialogTabCycle existed,
    // DialogComposer never set Control_Next, so the whole tab chain was NULL and a keyboard-only
    // user was stuck on the seeded FalkForgeRMOption=UseRM default with no way to reach
    // ShutdownOption (and therefore no way to decline having their applications terminated).
    [Fact]
    public void Compose_shutdown_option_is_reachable_by_tab_from_control_first()
    {
        var model = DialogComposer.Compose(MsiRMFilesInUseDlgBuilder.Build(), Layouts.Standard370x270);
        DialogTabCycle.Assign(model);

        string current = model.FirstControl;
        bool reached = false;
        for (int hop = 0; hop < model.Controls.Count; hop++)
        {
            if (current == "ShutdownOption")
            {
                reached = true;
                break;
            }

            var control = model.Controls.SingleOrDefault(c => c.Name == current);
            if (control?.NextControl is null)
            {
                break;
            }

            current = control.NextControl;
        }

        Assert.True(
            reached,
            "ShutdownOption must be reachable via Tab from Control_First, or a keyboard-only user " +
            "cannot decline the default 'close applications' choice.");
    }
}
