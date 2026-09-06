using System;
using System.Linq;
using System.Reflection;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Models;

public sealed class DialogCustomizationTests
{
    [Fact]
    public void BannerBitmap_with_null_throws()
    {
        var c = new DialogCustomization();
        Assert.Throws<ArgumentNullException>(() => c.BannerBitmap(null!));
    }

    [Fact]
    public void BannerBitmap_with_whitespace_throws()
    {
        var c = new DialogCustomization();
        Assert.Throws<ArgumentException>(() => c.BannerBitmap("   "));
    }

    [Fact]
    public void OverrideButtonLabel_overwrites_existing_label()
    {
        var c = new DialogCustomization()
            .OverrideButtonLabel(DialogButton.Next, "First")
            .OverrideButtonLabel(DialogButton.Next, "Second");

        var model = c.ToModel();

        Assert.Equal("Second", model.ButtonLabelOverrides[DialogButton.Next]);
        Assert.Single(model.ButtonLabelOverrides);
    }

    // SuppressDialog is gated with [Obsolete(error: true)] (task #24 — see task #44 for the real
    // implementation), so it can no longer be exercised from a fluent chain in this assembly; the
    // former SuppressDialog_is_idempotent coverage of _suppressed/ToModel plumbing is superseded
    // by DialogCustomizationSuppressDialogGateTests, which proves the call site itself refuses to
    // compile. SuppressedDialogs itself is still covered directly (object-initializer / with-expr)
    // by DialogCustomizationModelTests, since that path deliberately stays open for #44 and is
    // rejected at the validator layer (DLG002), not at the model layer.

    [Fact]
    public void ToModel_freezes_into_immutable_with_all_fields_preserved()
    {
        var c = new DialogCustomization()
            .BannerBitmap("banner.bmp")
            .DialogBitmap("bg.bmp")
            .HeaderIcon("icon.ico")
            .WindowTitle("My Setup")
            .OverrideButtonLabel(DialogButton.Install, "Begin")
            .OverrideButtonLabel(DialogButton.Cancel, "Abort");

        var model = c.ToModel();

        Assert.Equal("banner.bmp", model.BannerBitmap);
        Assert.Equal("bg.bmp", model.DialogBitmap);
        Assert.Equal("icon.ico", model.HeaderIcon);
        Assert.Equal("My Setup", model.WindowTitle);
        Assert.Equal("Begin", model.ButtonLabelOverrides[DialogButton.Install]);
        Assert.Equal("Abort", model.ButtonLabelOverrides[DialogButton.Cancel]);
    }

    [Fact]
    public void ToModel_called_twice_returns_independent_models()
    {
        var c = new DialogCustomization()
            .BannerBitmap("banner.bmp")
            .OverrideButtonLabel(DialogButton.Next, "Continue");

        var first = c.ToModel();

        // Mutate builder afterwards.
        c.BannerBitmap("new-banner.bmp")
            .OverrideButtonLabel(DialogButton.Next, "Forward");

        var second = c.ToModel();

        // First snapshot must be unaffected.
        Assert.Equal("banner.bmp", first.BannerBitmap);
        Assert.Equal("Continue", first.ButtonLabelOverrides[DialogButton.Next]);

        // Second reflects the mutation.
        Assert.Equal("new-banner.bmp", second.BannerBitmap);
        Assert.Equal("Forward", second.ButtonLabelOverrides[DialogButton.Next]);
    }

    [Fact]
    public void InsertStep_with_null_name_throws()
    {
        var c = new DialogCustomization();
        Assert.Throws<ArgumentNullException>(() => c.InsertStep(null!, DialogStepAnchor.License));
    }

    [Fact]
    public void InsertStep_with_whitespace_name_throws()
    {
        var c = new DialogCustomization();
        Assert.Throws<ArgumentException>(() => c.InsertStep("   ", DialogStepAnchor.License));
    }

    [Fact]
    public void InsertStep_records_step_in_model()
    {
        var c = new DialogCustomization()
            .InsertStep("LicenseKeyDlg", DialogStepAnchor.License);

        var model = c.ToModel();

        Assert.Single(model.InsertedSteps);
        Assert.Equal("LicenseKeyDlg", model.InsertedSteps[0].StepName);
        Assert.Equal(DialogStepAnchor.License, model.InsertedSteps[0].After);
    }

    [Fact]
    public void InsertStep_multiple_steps_preserves_order()
    {
        var c = new DialogCustomization()
            .InsertStep("StepA", DialogStepAnchor.Welcome)
            .InsertStep("StepB", DialogStepAnchor.License)
            .InsertStep("StepC", DialogStepAnchor.License);

        var model = c.ToModel();

        Assert.Equal(3, model.InsertedSteps.Length);
        Assert.Equal("StepA", model.InsertedSteps[0].StepName);
        Assert.Equal("StepB", model.InsertedSteps[1].StepName);
        Assert.Equal("StepC", model.InsertedSteps[2].StepName);
    }

    [Fact]
    public void InsertStep_snapshot_is_independent_of_later_mutations()
    {
        var c = new DialogCustomization()
            .InsertStep("StepA", DialogStepAnchor.License);

        var first = c.ToModel();

        c.InsertStep("StepB", DialogStepAnchor.Welcome);
        var second = c.ToModel();

        Assert.Single(first.InsertedSteps);
        Assert.Equal(2, second.InsertedSteps.Length);
    }

    // ── DialogStepAnchor, the insertion anchor that replaces StockDialog ──────────────

    [Fact]
    public void InsertStep_records_the_anchor_in_the_model()
    {
        var c = new DialogCustomization()
            .InsertStep("LicenseKeyDlg", DialogStepAnchor.License);

        var model = c.ToModel();

        Assert.Single(model.InsertedSteps);
        Assert.Equal("LicenseKeyDlg", model.InsertedSteps[0].StepName);
        Assert.Equal(DialogStepAnchor.License, model.InsertedSteps[0].After);
    }

    [Fact]
    public void InsertStep_keeps_call_order_for_two_steps_after_one_anchor()
    {
        // Order is call order and nothing sorts it, so two steps after the same anchor appear in
        // the wizard in the order the author wrote them.
        var model = new DialogCustomization()
            .InsertStep("SecondPage", DialogStepAnchor.Welcome)
            .InsertStep("FirstPage", DialogStepAnchor.Welcome)
            .ToModel();

        Assert.Equal(["SecondPage", "FirstPage"], model.InsertedSteps.Select(s => s.StepName));
    }

    [Fact]
    public void DialogStepAnchor_names_only_dialogs_that_can_host_a_step()
    {
        // StockDialog was built to name dialogs that could be suppressed, not dialogs a step can
        // follow. Four of its members map to no dialog at all, ProgressDlg is modeless with no
        // Next control, ExitDlg comes after the install, and the two most plausible anchors,
        // InstallScopeDlg, has no member. This enum lists exactly the dialogs a step can be
        // spliced in after, plus BeforeInstall for the set-independent case. The setup-type dialog
        // is absent on purpose: it has no Next control and three outgoing edges, so a step after it
        // has no single continuation to inherit.
        var members = Enum.GetNames<DialogStepAnchor>();

        Assert.Equal(
            ["Welcome", "License", "InstallScope", "InstallDir", "Features", "BeforeInstall"],
            members);
    }

    [Fact]
    public void The_StockDialog_overload_of_InsertStep_is_gated_off()
    {
        // StockDialog could name anchors that silently did nothing, so the overload is rejected at
        // compile time rather than left to look like a working call, matching how SuppressDialog is
        // gated. Reflection rather than a call site, because the call would not compile.
        var method = typeof(DialogCustomization)
            .GetMethods()
            .Single(m => m.Name == nameof(DialogCustomization.InsertStep)
                && m.GetParameters()[1].ParameterType == typeof(StockDialog));

        var obsolete = method.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.True(obsolete.IsError);
    }
}
