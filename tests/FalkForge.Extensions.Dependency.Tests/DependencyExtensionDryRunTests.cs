using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyExtensionDryRunTests
{
    [Fact]
    public void DependencyExtension_ImplementsIDryRunContributor()
    {
        var ext = new DependencyExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new DependencyExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new DependencyExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new DependencyExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Install_AllActionsHaveRegistryKind()
    {
        IDryRunContributor contributor = new DependencyExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.All(actions, a => Assert.Equal(DryRunActionKind.Registry, a.Kind));
    }
}
