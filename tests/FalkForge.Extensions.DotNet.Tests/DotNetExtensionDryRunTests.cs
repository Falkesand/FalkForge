using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.DotNet.Tests;

public sealed class DotNetExtensionDryRunTests
{
    [Fact]
    public void DotNetExtension_ImplementsIDryRunContributor()
    {
        var ext = new DotNetExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new DotNetExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new DotNetExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new DotNetExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Install_AllActionsHaveFileSystemKind()
    {
        IDryRunContributor contributor = new DotNetExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.All(actions, a => Assert.Equal(DryRunActionKind.FileSystem, a.Kind));
    }
}
