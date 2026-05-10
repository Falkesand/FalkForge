using FalkForge.Extensibility;

namespace FalkForge.Extensions.Http.Tests;

internal sealed class SpyExtensionRegistry : IExtensionRegistry
{
    public List<IMsiTableContributor> TableContributors { get; } = [];

    public void RegisterTableContributor(IMsiTableContributor contributor)
        => TableContributors.Add(contributor);

    public void RegisterComponentContributor(IComponentContributor contributor) { }
    public void RegisterDryRunContributor(IDryRunContributor contributor) { }
    public void RegisterDialogStep(IDialogStepBuilder builder) { }
}
