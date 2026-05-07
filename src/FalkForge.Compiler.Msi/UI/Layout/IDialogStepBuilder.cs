namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Contract for a dialog step that can be composed into an MSI wizard flow.
/// Internal to <c>FalkForge.Compiler.Msi</c>; stock builders and extension-contributed
/// builders both implement this interface, allowing them to be stored and invoked
/// uniformly by the template pipeline.
/// </summary>
/// <remarks>
/// RFC Cycle 6, step 16. Extension authors who wish to contribute a dialog step
/// implement this interface and register an instance via
/// <c>DialogStepRegistry.Register</c> during the compilation phase.
/// The <see cref="Name"/> property is the stable identifier referenced by
/// <see cref="FalkForge.Models.InsertedDialogStep.StepName"/> in
/// <see cref="FalkForge.Models.DialogCustomizationModel.InsertedSteps"/>.
/// DLG001 validates that every referenced name is present in the registry.
/// </remarks>
internal interface IDialogStepBuilder
{
    /// <summary>
    /// Stable identifier for this step. Referenced by
    /// <see cref="FalkForge.Models.InsertedDialogStep.StepName"/>. Must be unique within
    /// a <see cref="DialogStepRegistry"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Builds the <see cref="MsiDialogModel"/> for this dialog step. Called once per
    /// dialog step composition by the template pipeline.
    /// </summary>
    /// <param name="context">
    /// The build context providing the package customization and the full step registry.
    /// </param>
    MsiDialogModel Build(DialogBuildContext context);
}
