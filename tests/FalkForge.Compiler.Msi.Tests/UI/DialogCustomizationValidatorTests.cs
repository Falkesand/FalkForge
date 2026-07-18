using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

/// <summary>
/// Tests for DLG001 (unknown insert step) and DLG002 (suppression breaks navigation).
/// RFC Cycle 6 — step 17.
/// </summary>
public sealed class DialogCustomizationValidatorTests
{
    // ── DLG001 — unknown inserted step name ───────────────────────────────────

    [Fact]
    public void DLG001_no_inserted_steps_returns_no_errors()
    {
        var customization = new DialogCustomizationModel();
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG001_known_step_name_returns_no_errors()
    {
        var customization = new DialogCustomizationModel
        {
            InsertedSteps = ImmutableArray.Create(
                new InsertedDialogStep("MyStep", StockDialog.License)),
        };

        var registry = new DialogStepRegistry();
        registry.Register(new StubDialogStepBuilder("MyStep"));

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG001_unknown_step_name_returns_error()
    {
        var customization = new DialogCustomizationModel
        {
            InsertedSteps = ImmutableArray.Create(
                new InsertedDialogStep("UnknownStep", StockDialog.License)),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Contains("DLG001", errors[0].Code);
        Assert.Contains("UnknownStep", errors[0].Message);
    }

    [Fact]
    public void DLG001_multiple_unknown_steps_returns_error_per_step()
    {
        var customization = new DialogCustomizationModel
        {
            InsertedSteps = ImmutableArray.Create(
                new InsertedDialogStep("StepA", StockDialog.License),
                new InsertedDialogStep("StepB", StockDialog.Welcome)),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("DLG001", e.Code));
    }

    [Fact]
    public void DLG001_mixed_known_and_unknown_reports_only_unknown()
    {
        var customization = new DialogCustomizationModel
        {
            InsertedSteps = ImmutableArray.Create(
                new InsertedDialogStep("Known", StockDialog.License),
                new InsertedDialogStep("Unknown", StockDialog.Welcome)),
        };

        var registry = new DialogStepRegistry();
        registry.Register(new StubDialogStepBuilder("Known"));

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Contains("Unknown", errors[0].Message);
    }

    // ── DLG002 — suppression breaks navigation ────────────────────────────────

    [Fact]
    public void DLG002_suppressing_non_navigated_dialog_returns_no_errors()
    {
        // Maintenance dialog is not part of the standard wizard sequence
        // for Minimal — suppressing it is safe.
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Maintenance),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG002_suppressing_welcome_dialog_returns_error()
    {
        // Welcome is the entry point for every template — suppressing it breaks
        // the Install sequence entry.
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Welcome),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.InstallDir, registry, []);

        Assert.True(errors.Count > 0);
        Assert.Contains(errors, e => e.Code.Contains("DLG002"));
    }

    [Fact]
    public void DLG002_suppressing_progress_dialog_returns_error()
    {
        // Progress is the target of the "Install" button click in every template.
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Progress),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.True(errors.Count > 0);
        Assert.Contains(errors, e => e.Code.Contains("DLG002"));
    }

    [Fact]
    public void DLG002_error_message_names_both_suppressed_dialog_and_referencing_dialog()
    {
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Progress),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        var dlg002 = errors.First(e => e.Code.Contains("DLG002"));
        Assert.Contains("Progress", dlg002.Message);
    }

    // ── DLG003 — bitmap/icon customization key must be a registered Binary ────

    [Fact]
    public void DLG003_no_bitmap_customization_returns_no_errors()
    {
        var customization = new DialogCustomizationModel();
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG003_banner_bitmap_key_matching_registered_binary_returns_no_errors()
    {
        var customization = new DialogCustomizationModel { BannerBitmap = "AcmeBanner" };
        var registry = new DialogStepRegistry();
        var binaries = new[] { new BinaryModel { Name = "AcmeBanner", SourcePath = "banner.bmp" } };

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, binaries);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG003_banner_bitmap_key_with_no_matching_binary_returns_error()
    {
        var customization = new DialogCustomizationModel { BannerBitmap = "MissingKey" };
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Equal("DLG003", errors[0].Code);
        Assert.Contains("MissingKey", errors[0].Message);
        Assert.Contains("BannerBitmap", errors[0].Message);
    }

    [Fact]
    public void DLG003_dialog_bitmap_key_with_no_matching_binary_returns_error()
    {
        var customization = new DialogCustomizationModel { DialogBitmap = "MissingKey" };
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Equal("DLG003", errors[0].Code);
        Assert.Contains("MissingKey", errors[0].Message);
        Assert.Contains("DialogBitmap", errors[0].Message);
    }

    [Fact]
    public void DLG003_header_icon_key_with_no_matching_binary_returns_error()
    {
        var customization = new DialogCustomizationModel { HeaderIcon = "MissingIcon" };
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Equal("DLG003", errors[0].Code);
        Assert.Contains("MissingIcon", errors[0].Message);
        Assert.Contains("HeaderIcon", errors[0].Message);
    }

    [Fact]
    public void DLG003_all_three_keys_missing_returns_error_per_key()
    {
        var customization = new DialogCustomizationModel
        {
            BannerBitmap = "MissingBanner",
            DialogBitmap = "MissingDialog",
            HeaderIcon = "MissingIcon",
        };
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Equal(3, errors.Count);
        Assert.All(errors, e => Assert.Equal("DLG003", e.Code));
    }

    [Fact]
    public void DLG003_key_match_is_case_sensitive()
    {
        // MSI Binary.Name lookups are exact-match (Ordinal) throughout this codebase
        // (BinaryTableProducer keys streams by the literal Name string) — a case-mismatched
        // key must still be reported, not silently accepted.
        var customization = new DialogCustomizationModel { BannerBitmap = "AcmeBanner" };
        var registry = new DialogStepRegistry();
        var binaries = new[] { new BinaryModel { Name = "acmebanner", SourcePath = "banner.bmp" } };

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, binaries);

        Assert.Single(errors);
        Assert.Equal("DLG003", errors[0].Code);
    }

    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubDialogStepBuilder(string name) : IMsiDialogStepBuilder
    {
        public string Name => name;

        public MsiDialogModel Build(DialogBuildContext context)
            => new() { Name = Name, FirstControl = "Next" };
    }
}
