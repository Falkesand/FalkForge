using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Compiler-side extension of <see cref="IDialogStepBuilder"/> that adds the ability to
/// produce an <see cref="MsiDialogModel"/>. Stock dialog builders and extension-contributed
/// builders that need to control dialog layout implement this interface.
/// </summary>
/// <remarks>
/// <para>
/// RFC Cycle 6, step 16. Extension authors register an <see cref="IDialogStepBuilder"/>
/// via <see cref="FalkForge.Extensibility.IExtensionRegistry.RegisterDialogStep"/>. When
/// a registered builder also implements <see cref="IMsiDialogStepBuilder"/>, the template
/// pipeline invokes <see cref="Build"/> during compilation to obtain the full
/// <see cref="MsiDialogModel"/>. Builders that implement only <see cref="IDialogStepBuilder"/>
/// participate in DLG001 name-resolution but cannot supply dialog layout — their step name
/// passes validation but no dialog is emitted for them.
/// </para>
/// <para>
/// This interface is internal to <c>FalkForge.Compiler.Msi</c>. External extension
/// assemblies that need a full dialog must take a project reference to
/// <c>FalkForge.Compiler.Msi</c> and implement this interface directly.
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
