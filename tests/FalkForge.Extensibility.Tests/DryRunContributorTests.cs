using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensibility.Tests;

public sealed class DryRunContributorTests
{
    [Fact]
    public void DryRunAction_CanBeConstructed()
    {
        var action = new DryRunAction
        {
            Kind = DryRunActionKind.FileSystem,
            Description = "Would create directory C:\\MyApp"
        };

        Assert.Equal(DryRunActionKind.FileSystem, action.Kind);
        Assert.Equal("Would create directory C:\\MyApp", action.Description);
    }

    [Fact]
    public void DryRunIntent_HasExpectedValues()
    {
        _ = DryRunIntent.Install;
        _ = DryRunIntent.Uninstall;
        _ = DryRunIntent.Repair;
    }

    [Fact]
    public void DryRunActionKind_HasExpectedValues()
    {
        _ = DryRunActionKind.FileSystem;
        _ = DryRunActionKind.Registry;
        _ = DryRunActionKind.Network;
        _ = DryRunActionKind.Service;
        _ = DryRunActionKind.Database;
        _ = DryRunActionKind.Custom;
    }

    [Fact]
    public void IDryRunContributor_ContractTest()
    {
        // Verify the interface contract with a test implementation
        IDryRunContributor contributor = new TestDryRunContributor();
        var actions = contributor.GetDryRunActions(DryRunIntent.Install);

        Assert.Single(actions);
        Assert.Equal("Would install firewall rule", actions[0].Description);
    }

    private sealed class TestDryRunContributor : IDryRunContributor
    {
        public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
            [new DryRunAction { Kind = DryRunActionKind.Network, Description = "Would install firewall rule" }];
    }
}
