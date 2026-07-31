using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

/// <summary>
/// Unit tests for <see cref="DialogTabCycle"/>. Row-level coverage over the five stock
/// <c>MsiDialogSet</c> values (plus MsiRMFilesInUse) lives in
/// <c>DialogSetProducerTests</c>; these tests exercise <see cref="DialogTabCycle.Assign"/>
/// directly against a composed <see cref="MsiDialogModel"/>.
/// </summary>
public sealed class DialogTabCycleTests
{
    // The graph invariants (out-degree 1 / in-degree 1 / single orbit) are satisfied by ANY
    // permutation of the focusable set, including plain declaration order — so they cannot catch
    // a regression to a backwards button row. InstallDirDlg is the one dialog that exercises both
    // axes of the geometric ordering: a same-Y pair (Folder/ChangeFolder at Y=80, resolved by X)
    // and a three-button ButtonRow whose declaration order (Cancel, Next, Back) is the reverse of
    // its RightPacked visual order (Back, Next, Cancel) — see RightPackedRegionLayout's remarks.
    [Fact]
    public void Assign_orders_the_cycle_top_to_bottom_then_left_to_right()
    {
        var model = DialogComposer.Compose(InstallDirDlgBuilder.Build(), Layouts.Standard370x270);

        DialogTabCycle.Assign(model);

        string Next(string name) => model.Controls.Single(c => c.Name == name).NextControl!;

        Assert.Equal("ChangeFolder", Next("Folder"));
        Assert.Equal("Back", Next("ChangeFolder"));
        Assert.Equal("Next", Next("Back"));
        Assert.Equal("Cancel", Next("Next"));
        Assert.Equal("Folder", Next("Cancel"));
    }

    // Mirrors WiX's own guard (firstControl != lastTabSymbol.Control): a dialog with only one
    // focusable control gets no self-loop. ExitDlg's Title/Description are Text and BottomLine is
    // Line (all non-focusable), leaving Finish as the sole focusable control.
    [Fact]
    public void Assign_leaves_next_control_null_when_only_one_control_can_take_focus()
    {
        var model = DialogComposer.Compose(ExitDlgBuilder.Build(), Layouts.Standard370x270);

        DialogTabCycle.Assign(model);

        var finish = model.Controls.Single(c => c.Name == "Finish");
        Assert.Null(finish.NextControl);
    }

    // MsiRMFilesInUse: Title/Description/Text are Text controls and BottomLine is a Line —
    // all four are non-focusable and must stay out of the cycle entirely (never linked to, never
    // pointing anywhere), unlike WiX which additionally marks List TabSkip="yes" — this repo
    // deliberately keeps List focusable (see DialogTabCycle's remarks).
    [Fact]
    public void Assign_leaves_non_focusable_controls_out_of_the_cycle()
    {
        var model = DialogComposer.Compose(MsiRMFilesInUseDlgBuilder.Build(), Layouts.Standard370x270);
        string[] nonFocusable = ["Title", "Description", "Text", "BottomLine"];

        DialogTabCycle.Assign(model);

        foreach (string name in nonFocusable)
        {
            Assert.Null(model.Controls.Single(c => c.Name == name).NextControl);
        }

        foreach (var control in model.Controls)
        {
            Assert.DoesNotContain(control.NextControl, nonFocusable);
        }
    }

    // All-or-nothing guard: completing a partial chain automatically is how a broken half-cycle
    // gets manufactured, so ANY authored Control_Next on ANY control opts the WHOLE dialog out.
    [Fact]
    public void Assign_does_not_touch_a_dialog_whose_author_supplied_a_chain()
    {
        var model = new MsiDialogModel { Name = "PartialDlg", FirstControl = "A" };
        model.Controls.Add(new MsiControlModel { Name = "A", Type = MsiControlType.PushButton, NextControl = "B" });
        model.Controls.Add(new MsiControlModel { Name = "B", Type = MsiControlType.PushButton });
        model.Controls.Add(new MsiControlModel { Name = "C", Type = MsiControlType.PushButton });

        DialogTabCycle.Assign(model);

        Assert.Equal("B", model.Controls[0].NextControl);
        Assert.Null(model.Controls[1].NextControl);
        Assert.Null(model.Controls[2].NextControl);
    }
}
