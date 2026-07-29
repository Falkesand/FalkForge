using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

// IExtensionRegistry implementation used to collect extension contributions during Step 1.
public static partial class MsiAuthoring
{
    /// <summary>
    /// <see cref="IExtensionRegistry"/> implementation that collects registered
    /// extension contributions (table contributors, component contributors, dialog
    /// step builders) for batch processing after every extension has registered.
    /// </summary>
    private sealed class CollectingExtensionRegistry : IExtensionRegistry
    {
        public List<IDialogStepBuilder> DialogStepBuilders { get; } = [];

        public List<IMsiTableContributor> TableContributors { get; } = [];

        public List<IComponentContributor> ComponentContributors { get; } = [];

        public List<IExecutionContributor> ExecutionContributors { get; } = [];

        public void RegisterDialogStep(IDialogStepBuilder builder)
            => DialogStepBuilders.Add(builder);

        public void RegisterTableContributor(IMsiTableContributor contributor)
            => TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor)
            => ComponentContributors.Add(contributor);

        public void RegisterExecutionContributor(IExecutionContributor contributor)
            => ExecutionContributors.Add(contributor);

        // Required by IExtensionRegistry, but genuinely inert: dry-run previews are produced by
        // 'forge build --dry-run' / 'forge validate', which iterate the extension list directly
        // and call IDryRunContributor.GetDryRunActions themselves. Retaining the registrations
        // here would build a list nothing ever reads, so registration is accepted and dropped.
        public void RegisterDryRunContributor(IDryRunContributor contributor)
        {
            // Intentionally empty — see comment above.
        }
    }
}
