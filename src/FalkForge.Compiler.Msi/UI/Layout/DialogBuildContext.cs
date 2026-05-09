using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Immutable context passed to each <see cref="IMsiDialogStepBuilder.Build"/> invocation.
/// Carries the active customization model and the registry of all registered step builders.
/// </summary>
/// <remarks>
/// RFC Cycle 6, step 16. Templates create a context from the package model and registry,
/// then pass it to each step builder. Use <see cref="ForTest"/> in unit tests to create
/// a minimal context without a full package model.
/// </remarks>
internal sealed class DialogBuildContext
{
    /// <summary>
    /// The active dialog customization: branding, button overrides, suppression set,
    /// and the list of inserted extension steps.
    /// </summary>
    public DialogCustomizationModel Customization { get; }

    /// <summary>
    /// Registry of all <see cref="IMsiDialogStepBuilder"/> instances available in this
    /// compilation context. Templates use this to resolve
    /// <see cref="DialogCustomizationModel.InsertedSteps"/> by name.
    /// </summary>
    public DialogStepRegistry StepRegistry { get; }

    private DialogBuildContext(DialogCustomizationModel customization, DialogStepRegistry stepRegistry)
    {
        Customization = customization;
        StepRegistry = stepRegistry;
    }

    /// <summary>
    /// Creates a <see cref="DialogBuildContext"/> from a package model and a frozen
    /// step registry. Used by templates and by <see cref="MsiAuthoring"/> at compile time.
    /// </summary>
    public static DialogBuildContext Create(DialogCustomizationModel customization, DialogStepRegistry stepRegistry)
    {
        ArgumentNullException.ThrowIfNull(customization);
        ArgumentNullException.ThrowIfNull(stepRegistry);
        return new DialogBuildContext(customization, stepRegistry);
    }

    /// <summary>
    /// Creates a <see cref="DialogBuildContext"/> with an empty step registry for use in
    /// unit tests that do not need extension steps.
    /// </summary>
    public static DialogBuildContext ForTest(DialogCustomizationModel customization)
    {
        ArgumentNullException.ThrowIfNull(customization);
        return new DialogBuildContext(customization, new DialogStepRegistry());
    }
}
