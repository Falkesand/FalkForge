using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class IisExtensionDryRunTests
{
    [Fact]
    public void IisExtension_ImplementsIDryRunContributor()
    {
        var ext = new IisExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new IisExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new IisExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new IisExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Install_AllActionsHaveNetworkKind()
    {
        IDryRunContributor contributor = new IisExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.All(actions, a => Assert.Equal(DryRunActionKind.Network, a.Kind));
    }
}
