namespace FalkForge.Extensibility;

/// <summary>
/// Marker interface for an extension-contributed dialog step. Extensions implement this
/// interface and register an instance via <see cref="IExtensionRegistry.RegisterDialogStep"/>
/// during their <see cref="IFalkForgeExtension.Register"/> callback.
/// </summary>
/// <remarks>
/// <para>
/// RFC Cycle 6, step 16. The <see cref="Name"/> is the stable identifier referenced by
/// <c>DialogCustomization.InsertStep(stepName, after:)</c> in <c>FalkForge.Models</c>.
/// DLG001 validation in <c>FalkForge.Compiler.Msi</c> rejects any <c>InsertStep</c> call
/// whose <paramref name="Name"/> is not present in the <c>DialogStepRegistry</c> at compile time.
/// </para>
/// <para>
/// Extension authors who need to produce a full <see cref="MsiDialogModel"/> should implement
/// <c>IMsiDialogStepBuilder</c> (defined in <c>FalkForge.Compiler.Msi</c>), which extends
/// this interface with a <c>Build(DialogBuildContext)</c> method. That requires a project
/// reference to <c>FalkForge.Compiler.Msi</c>.
/// </para>
/// </remarks>
public interface IDialogStepBuilder
{
    /// <summary>
    /// Stable identifier for this dialog step. Referenced by
    /// <c>DialogCustomization.InsertStep(stepName, after:)</c>.
    /// Must be unique within a compilation context.
    /// </summary>
    string Name { get; }
}
