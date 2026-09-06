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

    /// <summary>
    /// Where this step sits in the wizard chain once it has been spliced in: the dialog its Back
    /// returns to, and the dialog its Next advances to. A step cannot work this out for itself,
    /// because the chain depends on the active dialog set and on where the author anchored it.
    /// </summary>
    /// <remarks>
    /// A builder that ignores this and hardcodes its own <see cref="DialogFlowContext"/> emits a
    /// NewDialog with an empty argument and produces a page the user cannot advance past. The
    /// compiler checks for that rather than trusting the builder.
    /// </remarks>
    public DialogFlowContext Flow { get; }

    private DialogBuildContext(
        DialogCustomizationModel customization,
        DialogStepRegistry stepRegistry,
        DialogFlowContext flow)
    {
        Customization = customization;
        StepRegistry = stepRegistry;
        Flow = flow;
    }

    /// <summary>
    /// Creates a <see cref="DialogBuildContext"/> from a package model and a frozen
    /// step registry. Used by templates and by <see cref="MsiAuthoring"/> at compile time.
    /// </summary>
    public static DialogBuildContext Create(
        DialogCustomizationModel customization,
        DialogStepRegistry stepRegistry,
        DialogFlowContext? flow = null)
    {
        ArgumentNullException.ThrowIfNull(customization);
        ArgumentNullException.ThrowIfNull(stepRegistry);
        return new DialogBuildContext(customization, stepRegistry, flow ?? new DialogFlowContext());
    }

    /// <summary>
    /// Creates a <see cref="DialogBuildContext"/> with an empty step registry for use in
    /// unit tests that do not need extension steps.
    /// </summary>
    public static DialogBuildContext ForTest(DialogCustomizationModel customization)
    {
        ArgumentNullException.ThrowIfNull(customization);
        return new DialogBuildContext(customization, new DialogStepRegistry(), new DialogFlowContext());
    }
}
