namespace FalkForge.Extensibility;

/// <summary>
/// Marker interface for an extension-contributed dialog step. Extensions implement this
/// interface and register an instance via <see cref="IExtensionRegistry.RegisterDialogStep"/>
/// during their <see cref="IFalkForgeExtension.Register"/> callback.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Name"/> is the stable identifier referenced by
/// <c>DialogCustomization.InsertStep(stepName, after:)</c> in <c>FalkForge.Models</c>.
/// DLG001 validation in <c>FalkForge.Compiler.Msi</c> rejects any <c>InsertStep</c> call
/// whose <see cref="Name"/> is not registered at compile time.
/// </para>
/// <para>
/// An extension that only implements this interface reserves its step name (so <c>InsertStep</c>
/// passes validation) but emits no dialog. To actually contribute dialog layout that the compiler
/// emits, implement <c>IMsiDialogStepBuilder</c> (defined in <c>FalkForge.Compiler.Msi</c>), which
/// extends this interface with a <c>Build(DialogBuildContext)</c> method; that requires a project
/// reference to <c>FalkForge.Compiler.Msi</c>.
/// </para>
/// <para>
/// Most authors do not need this interface at all: to author a complete custom dialog from
/// application code, use
/// <c>PackageBuilder.AddCustomDialog(id, dlg =&gt; …)</c> in <c>FalkForge.Core</c>, which builds the
/// dialog directly with no extension plumbing.
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
