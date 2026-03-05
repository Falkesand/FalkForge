using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Util.Tests;

public sealed class UtilExtensionDryRunTests
{
    [Fact]
    public void UtilExtension_ImplementsIDryRunContributor()
    {
        var ext = new UtilExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new UtilExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new UtilExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new UtilExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }
}
