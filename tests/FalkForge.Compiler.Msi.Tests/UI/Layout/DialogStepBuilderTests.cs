using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

/// <summary>
/// Tests for IMsiDialogStepBuilder, DialogBuildContext, and DialogStepRegistry.
/// RFC Cycle 6 — step 16.
/// </summary>
public sealed class DialogStepBuilderTests
{
    [Fact]
    public void DialogBuildContext_ForTest_creates_context_with_no_steps()
    {
        var context = DialogBuildContext.ForTest(
            new DialogCustomizationModel());

        Assert.NotNull(context);
        Assert.Empty(context.StepRegistry);
    }

    [Fact]
    public void DialogBuildContext_ForTest_with_customization_exposes_it()
    {
        var customization = new DialogCustomizationModel
        {
            BannerBitmap = "banner.bmp",
        };

        var context = DialogBuildContext.ForTest(customization);

        Assert.Equal("banner.bmp", context.Customization.BannerBitmap);
    }

    [Fact]
    public void DialogStepRegistry_Register_stores_builder_by_name()
    {
        var registry = new DialogStepRegistry();
        var stub = new StubDialogStepBuilder("MyStep");

        registry.Register(stub);

        Assert.True(registry.TryGet("MyStep", out var retrieved));
        Assert.Same(stub, retrieved);
    }

    [Fact]
    public void DialogStepRegistry_TryGet_returns_false_for_unknown_name()
    {
        var registry = new DialogStepRegistry();

        var found = registry.TryGet("Unknown", out var retrieved);

        Assert.False(found);
        Assert.Null(retrieved);
    }

    [Fact]
    public void DialogStepRegistry_Register_duplicate_name_throws()
    {
        var registry = new DialogStepRegistry();
        registry.Register(new StubDialogStepBuilder("Dup"));

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new StubDialogStepBuilder("Dup")));
    }

    [Fact]
    public void DialogStepRegistry_Freeze_prevents_further_registration()
    {
        var registry = new DialogStepRegistry();
        registry.Register(new StubDialogStepBuilder("First"));
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new StubDialogStepBuilder("Second")));
    }

    [Fact]
    public void DialogStepRegistry_Enumerate_returns_all_registered_builders()
    {
        var registry = new DialogStepRegistry();
        registry.Register(new StubDialogStepBuilder("A"));
        registry.Register(new StubDialogStepBuilder("B"));

        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void IMsiDialogStepBuilder_Build_produces_dialog_model_with_expected_name()
    {
        var stub = new StubDialogStepBuilder("LicenseKeyDlg");
        var context = DialogBuildContext.ForTest(new DialogCustomizationModel());

        var model = stub.Build(context);

        Assert.Equal("LicenseKeyDlg", model.Name);
    }

    // ── Stub implementation ───────────────────────────────────────────────────

    private sealed class StubDialogStepBuilder(string name) : IMsiDialogStepBuilder
    {
        public string Name => name;

        public MsiDialogModel Build(DialogBuildContext context)
        {
            System.ArgumentNullException.ThrowIfNull(context);

            return new MsiDialogModel
            {
                Name = Name,
                FirstControl = "Next",
            };
        }
    }
}
