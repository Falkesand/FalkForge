using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Compiler-side extension of <see cref="IDialogStepBuilder"/> that adds the ability to
/// produce an <see cref="MsiDialogModel"/>. Stock dialog builders and extension-contributed
/// builders that need to control dialog layout implement this interface.
/// </summary>
/// <remarks>
/// <para>
/// Extension authors register an <see cref="IDialogStepBuilder"/> via
/// <see cref="FalkForge.Extensibility.IExtensionRegistry.RegisterDialogStep"/> and reference it
/// from <c>DialogCustomization.InsertStep(stepName, after:)</c>. When a registered builder also
/// implements <see cref="IMsiDialogStepBuilder"/>, <c>DialogSetProducer</c> invokes
/// <see cref="Build"/> during compilation and emits the resulting dialog into the MSI UI tables.
/// A builder that implements only <see cref="IDialogStepBuilder"/> passes DLG001 name-resolution
/// but supplies no layout, so no dialog is emitted for it.
/// </para>
/// <para>
/// This interface is internal to <c>FalkForge.Compiler.Msi</c> because it exposes the internal
/// <see cref="MsiDialogModel"/>. Most authors do not need it: to author a complete custom dialog
/// from application code, use the public
/// <see cref="FalkForge.Builders.PackageBuilder.AddCustomDialog(string, System.Action{FalkForge.Builders.CustomDialogBuilder})"/>
/// API, which requires no reference to this assembly. This interface exists for extensions that
/// take a project reference to <c>FalkForge.Compiler.Msi</c> and want to contribute a reusable,
/// named dialog step insertable via <c>InsertStep</c>.
/// </para>
/// </remarks>
internal interface IMsiDialogStepBuilder : IDialogStepBuilder
{
    /// <summary>
    /// Builds the <see cref="MsiDialogModel"/> for this dialog step. Called once per
    /// step composition by the template pipeline.
    /// </summary>
    /// <param name="context">
    /// Build context providing the active customization model and the full step registry.
    /// </param>
    MsiDialogModel Build(DialogBuildContext context);
}
