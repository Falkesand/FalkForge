using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests;

public sealed class SqlExtensionDryRunTests
{
    [Fact]
    public void SqlExtension_ImplementsIDryRunContributor()
    {
        var ext = new SqlExtension();
        Assert.IsAssignableFrom<IDryRunContributor>(ext);
    }

    [Fact]
    public void GetDryRunActions_Install_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new SqlExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Uninstall_ReturnsNonEmptyList()
    {
        IDryRunContributor contributor = new SqlExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Uninstall);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotEmpty(a.Description));
    }

    [Fact]
    public void GetDryRunActions_Repair_ReturnsEmptyList()
    {
        IDryRunContributor contributor = new SqlExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Repair);

        Assert.Empty(actions);
    }

    [Fact]
    public void GetDryRunActions_Install_AllActionsHaveDatabaseKind()
    {
        IDryRunContributor contributor = new SqlExtension();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.All(actions, a => Assert.Equal(DryRunActionKind.Database, a.Kind));
    }
}
