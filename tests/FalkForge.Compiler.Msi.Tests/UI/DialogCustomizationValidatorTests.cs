using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

/// <summary>
/// Tests for DLG001 (unknown insert step) and DLG002 (SuppressDialog is not implemented —
/// task #24/#44 — so any non-empty SuppressedDialogs fails the build).
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

    // ── DLG002 — SuppressDialog is not implemented (task #24 / task #44) ──────
    //
    // SuppressDialog used to be validated against a per-template "protected dialog"
    // navigation-target table (ProtectedDialogs/BuildProtectedDialogs). That table was itself
    // wrong — DialogCustomizationValidator's InstallDir entry omitted License even though
    // InstallDirDialogTemplate wires Welcome->License->InstallDir — and, worse, nothing
    // downstream ever consumed SuppressedDialogs at all: DialogComposer explicitly declines to
    // apply it, and every dialog-set template composes every stock dialog unconditionally. So
    // the old DLG002 let some suppressions through as "safe" when in truth *every* suppression
    // was a no-op the author had no way to detect. DLG002 now rejects any non-empty
    // SuppressedDialogs outright, regardless of which dialog or template.

    [Fact]
    public void DLG002_empty_suppressed_dialogs_returns_no_errors()
    {
        var customization = new DialogCustomizationModel();
        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Empty(errors);
    }

    [Fact]
    public void DLG002_any_suppressed_dialog_returns_error()
    {
        // Maintenance was NOT in the old ProtectedDialogs table for any template — it used to be
        // reported as "safe" to suppress even though suppression was never actually implemented.
        // This is the regression case: if DLG002 goes back to consulting a protected-dialog table
        // instead of rejecting unconditionally, this test goes red.
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Maintenance),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Single(errors);
        Assert.Equal("DLG002", errors[0].Code);
    }

    [Fact]
    public void DLG002_object_initializer_populated_SuppressedDialogs_is_rejected_without_the_builder()
    {
        // DialogCustomization.SuppressDialog() is gated with [Obsolete(error: true)], but
        // DialogCustomizationModel.SuppressedDialogs is a public init property on the model
        // itself — an author can populate it directly, never calling the obsoleted method at
        // all. This DLG002 check is the ONLY gate covering that path. Mutation check: delete the
        // "foreach (var suppressed in customization.SuppressedDialogs)" loop in
        // DialogCustomizationValidator.Validate and this test goes red.
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.License),
        };

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.FeatureTree, new DialogStepRegistry(), []);

        Assert.Contains(errors, e => e.Code == "DLG002");
    }

    [Fact]
    public void DLG002_multiple_suppressed_dialogs_returns_error_per_dialog()
    {
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Welcome, StockDialog.Progress),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("DLG002", e.Code));
    }

    [Fact]
    public void DLG002_error_message_names_the_suppressed_dialog_and_task_44()
    {
        var customization = new DialogCustomizationModel
        {
            SuppressedDialogs = ImmutableHashSet.Create(StockDialog.Progress),
        };

        var registry = new DialogStepRegistry();

        var errors = DialogCustomizationValidator.Validate(
            customization, MsiDialogSet.Minimal, registry, []);

        var dlg002 = errors.First(e => e.Code == "DLG002");
        Assert.Contains("Progress", dlg002.Message);
        Assert.Contains("#44", dlg002.Message);
        Assert.Contains("SuppressDialog", dlg002.Message);
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
