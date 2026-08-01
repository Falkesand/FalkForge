using System;
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
        Assert.Throws<ArgumentNullException>(() => c.InsertStep(null!, StockDialog.License));
    }

    [Fact]
    public void InsertStep_with_whitespace_name_throws()
    {
        var c = new DialogCustomization();
        Assert.Throws<ArgumentException>(() => c.InsertStep("   ", StockDialog.License));
    }

    [Fact]
    public void InsertStep_records_step_in_model()
    {
        var c = new DialogCustomization()
            .InsertStep("LicenseKeyDlg", StockDialog.License);

        var model = c.ToModel();

        Assert.Single(model.InsertedSteps);
        Assert.Equal("LicenseKeyDlg", model.InsertedSteps[0].StepName);
        Assert.Equal(StockDialog.License, model.InsertedSteps[0].After);
    }

    [Fact]
    public void InsertStep_multiple_steps_preserves_order()
    {
        var c = new DialogCustomization()
            .InsertStep("StepA", StockDialog.Welcome)
            .InsertStep("StepB", StockDialog.License)
            .InsertStep("StepC", StockDialog.License);

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
            .InsertStep("StepA", StockDialog.License);

        var first = c.ToModel();

        c.InsertStep("StepB", StockDialog.Welcome);
        var second = c.ToModel();

        Assert.Single(first.InsertedSteps);
        Assert.Equal(2, second.InsertedSteps.Length);
    }
}
