namespace FalkForge.Extensibility;

public interface IExtensionRegistry
{
    void RegisterTableContributor(IMsiTableContributor contributor);
    void RegisterComponentContributor(IComponentContributor contributor);
    void RegisterDryRunContributor(IDryRunContributor contributor);

    /// <summary>
    /// Registers an extension-contributed <see cref="IExecutionContributor"/> whose
    /// <see cref="ExecutionStep"/> declarations the MSI compiler turns into deferred, elevated
    /// custom actions (plus rollback/uninstall actions and sequence rows) so extension work
    /// actually runs at install time.
    /// <para>
    /// Defaulted to a no-op so existing <see cref="IExtensionRegistry"/> implementations (test
    /// doubles, alternate hosts) keep compiling unchanged; a host that supports install-time
    /// execution overrides it to collect the contributors.
    /// </para>
    /// </summary>
    void RegisterExecutionContributor(IExecutionContributor contributor) { }

    /// <summary>
    /// Registers an extension-contributed dialog step builder. The step becomes available
    /// for insertion via <c>DialogCustomization.InsertStep(name, after:)</c> at package build time.
    /// DLG001 validation will not fire for step names that are registered here.
    /// </summary>
    /// <param name="builder">
    /// The dialog step builder to register. Use an <c>IMsiDialogStepBuilder</c> (from
    /// <c>FalkForge.Compiler.Msi</c>) when your extension needs to produce a custom
    /// <c>MsiDialogModel</c>; a plain <see cref="IDialogStepBuilder"/> suffices for
    /// registration-only scenarios.
    /// </param>
    void RegisterDialogStep(IDialogStepBuilder builder);
}
