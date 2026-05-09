namespace FalkForge.Extensibility.Tests;

using FalkForge.Extensibility;
using Xunit;

/// <summary>
/// Tests for the <see cref="IExtensionRegistry.RegisterDialogStep"/> contract.
/// RFC Cycle 6 — step 16: extensions contribute dialog steps via the extension registry.
/// </summary>
public sealed class ExtensionContextDialogStepTests
{
    // ── Stubs ─────────────────────────────────────────────────────────────────

    private sealed class StubDialogStepBuilder(string name) : IDialogStepBuilder
    {
        public string Name => name;
    }

    private sealed class CollectingRegistry : IExtensionRegistry
    {
        public List<IDialogStepBuilder> DialogStepBuilders { get; } = [];

        public void RegisterDialogStep(IDialogStepBuilder builder)
            => DialogStepBuilders.Add(builder);

        public void RegisterTableContributor(IMsiTableContributor contributor) { }
        public void RegisterComponentContributor(IComponentContributor contributor) { }
        public void RegisterValidator(IExtensionValidator validator) { }
        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
    }

    private sealed class DialogStepExtension(string stepName) : IFalkForgeExtension
    {
        public string Name => "DialogStepExtension";
        public string Version => "1.0.0";
        public string? MinHostVersion => null;

        public void Register(IExtensionRegistry registry)
            => registry.RegisterDialogStep(new StubDialogStepBuilder(stepName));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RegisterDialogStep_extension_builder_lands_in_registry()
    {
        // Arrange
        var registry = new CollectingRegistry();
        var extension = new DialogStepExtension("LicenseKeyDlg");

        // Act
        extension.Register(registry);

        // Assert
        Assert.Single(registry.DialogStepBuilders);
        Assert.Equal("LicenseKeyDlg", registry.DialogStepBuilders[0].Name);
    }

    [Fact]
    public void RegisterDialogStep_multiple_builders_all_collected()
    {
        // Arrange
        var registry = new CollectingRegistry();

        // Act
        registry.RegisterDialogStep(new StubDialogStepBuilder("StepA"));
        registry.RegisterDialogStep(new StubDialogStepBuilder("StepB"));

        // Assert
        Assert.Equal(2, registry.DialogStepBuilders.Count);
        Assert.Contains(registry.DialogStepBuilders, b => b.Name == "StepA");
        Assert.Contains(registry.DialogStepBuilders, b => b.Name == "StepB");
    }

    [Fact]
    public void IDialogStepBuilder_Name_is_stable_identifier()
    {
        // Arrange
        IDialogStepBuilder builder = new StubDialogStepBuilder("MyDialog");

        // Assert
        Assert.Equal("MyDialog", builder.Name);
    }

    [Fact]
    public void RegisterDialogStep_via_ExtensionRegistration_helper_reaches_registry()
    {
        // Arrange — route through the full ExtensionRegistration helper to confirm
        // the dialog step registration flows through the standard extension pipeline.
        var registry = new CollectingRegistry();
        var registeredNames = new HashSet<string>(StringComparer.Ordinal);
        var extension = new DialogStepExtension("CompanyLicenseDlg");

        // Act
        ExtensionRegistration.Register(extension, registry, registeredNames);

        // Assert
        Assert.Single(registry.DialogStepBuilders);
        Assert.Equal("CompanyLicenseDlg", registry.DialogStepBuilders[0].Name);
    }
}
