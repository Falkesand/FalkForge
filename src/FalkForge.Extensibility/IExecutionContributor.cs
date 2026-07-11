namespace FalkForge.Extensibility;

/// <summary>
/// An extension contribution of install-time <b>execution</b>: a set of
/// <see cref="ExecutionStep"/> declarations that the MSI compiler turns into deferred,
/// elevated custom actions plus their rollback / uninstall actions and sequence rows.
///
/// <para>
/// This is the counterpart to <see cref="IMsiTableContributor"/>. A table contributor puts
/// <i>data</i> into the compiled MSI; an execution contributor makes that data <i>live</i>
/// by scheduling the work that acts on it at install time. An extension typically registers
/// both — the table for inspection/decompile record, the execution steps for behaviour.
/// </para>
///
/// <para>
/// Register an instance via <see cref="IExtensionRegistry.RegisterExecutionContributor"/>.
/// The compiler collects steps from every registered contributor in registration order and
/// allocates their sequence numbers from a single deterministic pool, so multiple extensions
/// (Firewall, SQL, IIS, …) contributing execution in the same package never collide.
/// </para>
/// </summary>
public interface IExecutionContributor
{
    /// <summary>
    /// Returns the execution steps this contributor wants scheduled for the given package.
    /// Called once during compilation; return an empty list to contribute nothing. The order
    /// of the returned steps is preserved and drives deterministic sequence-number allocation.
    /// </summary>
    IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context);
}
