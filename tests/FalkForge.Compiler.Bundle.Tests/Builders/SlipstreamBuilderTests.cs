using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class SlipstreamBuilderTests
{
    [Fact]
    public void Build_WithSlipstreamTarget_SetsTargetId()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("Hotfix1")
                .SlipstreamTarget("MainMsi")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal("MainMsi", model.Packages[0].SlipstreamTargetId);
    }

    [Fact]
    public void Build_WithoutSlipstreamTarget_DefaultsNull()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("Hotfix1")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Null(model.Packages[0].SlipstreamTargetId);
    }
}
