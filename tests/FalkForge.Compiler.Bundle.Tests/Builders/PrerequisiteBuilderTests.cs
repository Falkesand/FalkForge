using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class PrerequisiteBuilderTests
{
    [Fact]
    public void Build_WithPrerequisite_SetsIsPrerequisite()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("MainApp")
                .Prerequisite()))
            .Build();

        Assert.Single(model.Packages);
        Assert.True(model.Packages[0].IsPrerequisite);
    }

    [Fact]
    public void Build_WithoutPrerequisite_DefaultsFalse()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("MainApp")))
            .Build();

        Assert.Single(model.Packages);
        Assert.False(model.Packages[0].IsPrerequisite);
    }
}
