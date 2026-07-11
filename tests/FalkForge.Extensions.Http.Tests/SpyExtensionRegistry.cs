using FalkForge.Extensibility;

namespace FalkForge.Extensions.Http.Tests;

internal sealed class SpyExtensionRegistry : IExtensionRegistry
{
    public List<IMsiTableContributor> TableContributors { get; } = [];
    public List<IExecutionContributor> ExecutionContributors { get; } = [];

    public void RegisterTableContributor(IMsiTableContributor contributor)
        => TableContributors.Add(contributor);

    public void RegisterComponentContributor(IComponentContributor contributor) { }
    public List<IDryRunContributor> DryRunContributors { get; } = [];

    public void RegisterDryRunContributor(IDryRunContributor contributor)
        => DryRunContributors.Add(contributor);

    public void RegisterExecutionContributor(IExecutionContributor contributor)
        => ExecutionContributors.Add(contributor);

    public void RegisterDialogStep(IDialogStepBuilder builder) { }
}
