using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

public sealed class FirewallExtensionDryRunTests
{
    [Fact]
    public void FirewallExtension_ImplementsIDryRunContributor()
    {
        var ext = new FirewallExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new FirewallExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new FirewallExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new FirewallExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Install_AllActionsHaveNetworkKind()
    {
        IDryRunContributor contributor = new FirewallExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.All(actions, a => Assert.Equal(DryRunActionKind.Network, a.Kind));
    }
}
