using System;
using System.Collections.Generic;
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

    // ADR 0007 section 5 claims ProgressDlg's NULL Control_Next is "proven by exact-row-level test
    // coverage" the same as ExitDlg's above — this pins that same guarantee for ProgressDlg's sole
    // focusable control (Title/StatusLabel/ActionText are Text and BottomLine is Line, all
    // non-focusable, leaving Cancel as the sole focusable control), so the claim is actually true
    // for both dialogs rather than only ExitDlg.
    [Fact]
    public void Assign_leaves_next_control_null_for_ProgressDlg_sole_focusable_control()
    {
        var model = DialogComposer.Compose(ProgressDlgBuilder.Build(), Layouts.Standard370x270);

        DialogTabCycle.Assign(model);

        var cancel = model.Controls.Single(c => c.Name == "Cancel");
        Assert.Null(cancel.NextControl);
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
    // gets manufactured, so ANY authored Control_Next on ANY control opts the WHOLE dialog out —
    // not just when the authored link happens to sit on Controls[0]. The authored link here sits
    // on Controls[2] specifically: a guard narrowed to "only Controls[0] disables the dialog"
    // (e.g. `if (controls[0].NextControl is not null) return;`) would NOT see it, so Assign would
    // proceed to auto-wire all three controls — overwriting the author's own C->A link and giving
    // A/B non-null values they should never receive. This test fails under that narrowing.
    [Fact]
    public void Assign_does_not_touch_a_dialog_whose_author_supplied_a_chain()
    {
        var model = new MsiDialogModel { Name = "PartialDlg", FirstControl = "A" };
        model.Controls.Add(new MsiControlModel { Name = "A", Type = MsiControlType.PushButton });
        model.Controls.Add(new MsiControlModel { Name = "B", Type = MsiControlType.PushButton });
        model.Controls.Add(new MsiControlModel { Name = "C", Type = MsiControlType.PushButton, NextControl = "A" });

        DialogTabCycle.Assign(model);

        Assert.Null(model.Controls[0].NextControl);
        Assert.Null(model.Controls[1].NextControl);
        Assert.Equal("A", model.Controls[2].NextControl);
    }

    // Empty-string / whitespace NextControl must NOT count as an authored chain: unlike a real
    // author-supplied link, it carries no intent and (via DialogSetProducer.Rows.cs's StringOrNull)
    // would otherwise emit a Control_Next cell pointing at a control literally named "" — a value
    // DLG017's IsNullOrWhiteSpace guard does not catch either. Assign must treat it as absent and
    // proceed to auto-wire (overwriting the blank placeholder), not opt the whole dialog out.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Assign_treats_empty_or_whitespace_NextControl_as_unauthored(string blank)
    {
        var model = new MsiDialogModel { Name = "BlankDlg", FirstControl = "A" };
        model.Controls.Add(new MsiControlModel { Name = "A", Type = MsiControlType.PushButton, NextControl = blank });
        model.Controls.Add(new MsiControlModel { Name = "B", Type = MsiControlType.PushButton });

        DialogTabCycle.Assign(model);

        Assert.Equal("B", model.Controls[0].NextControl);
        Assert.Equal("A", model.Controls[1].NextControl);
    }

    // The focusable/non-focusable classifier (here and in TabCycleAssert) both default an
    // unrecognized MsiControlType to focusable (`_ => true`). Adding a new enum member without
    // deciding its focusability would have BOTH classifiers silently agree it is a tab stop,
    // shipping a dead tab stop with no test noticing. This test pins the full membership of each
    // set so Enum.GetValues<MsiControlType>() growing without a matching update to one of these
    // two sets fails here first.
    [Fact]
    public void Every_MsiControlType_member_is_explicitly_classified_as_focusable_or_not()
    {
        var nonFocusable = new HashSet<MsiControlType>
        {
            MsiControlType.Text,
            MsiControlType.Line,
            MsiControlType.Bitmap,
            MsiControlType.Icon,
            MsiControlType.ProgressBar,
            MsiControlType.GroupBox,
            MsiControlType.VolumeCostList,
        };
        var focusable = new HashSet<MsiControlType>
        {
            MsiControlType.PushButton,
            MsiControlType.CheckBox,
            MsiControlType.ScrollableText,
            MsiControlType.PathEdit,
            MsiControlType.SelectionTree,
            MsiControlType.RadioButtonGroup,
            MsiControlType.ComboBox,
            MsiControlType.Edit,
            MsiControlType.ListBox,
            MsiControlType.DirectoryCombo,
            MsiControlType.DirectoryList,
            MsiControlType.MaskedEdit,
        };

        var all = new HashSet<MsiControlType>(Enum.GetValues<MsiControlType>());

        Assert.Empty(nonFocusable.Intersect(focusable));
        Assert.True(
            all.SetEquals(nonFocusable.Union(focusable)),
            "MsiControlType has a member not explicitly classified as focusable or non-focusable in this test " +
            "(and, if a real new member, in DialogTabCycle.IsFocusable / TabCycleAssert too).");
    }
}
