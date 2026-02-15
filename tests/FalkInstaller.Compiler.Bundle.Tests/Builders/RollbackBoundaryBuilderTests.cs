namespace FalkInstaller.Compiler.Bundle.Tests.Builders;

using FalkInstaller.Compiler.Bundle.Builders;
using Xunit;

public sealed class RollbackBoundaryBuilderTests
{
    [Fact]
    public void Build_DefaultVital_IsTrue()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .RollbackBoundary("RB1")
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        Assert.Single(builder.Chain.OfType<RollbackBoundaryChainItem>());
        var boundary = builder.Chain.OfType<RollbackBoundaryChainItem>().First();
        Assert.True(boundary.Boundary.Vital);
    }

    [Fact]
    public void Build_VitalFalse_SetsCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .RollbackBoundary("RB1", rb => rb.Vital(false))
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        var boundary = model.Chain.OfType<RollbackBoundaryChainItem>().First();
        Assert.False(boundary.Boundary.Vital);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .RollbackBoundary("MyBoundary")
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        var boundary = model.Chain.OfType<RollbackBoundaryChainItem>().First();
        Assert.Equal("MyBoundary", boundary.Boundary.Id);
    }

    [Fact]
    public void Build_ProducesCorrectModel()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .RollbackBoundary("RB1", rb => rb.Vital(false))
                .MsiPackage("first.msi", p => p.Id("First"))
                .RollbackBoundary("RB2")
                .MsiPackage("second.msi", p => p.Id("Second")))
            .Build();

        Assert.Equal(4, model.Chain.Count);
        Assert.IsType<RollbackBoundaryChainItem>(model.Chain[0]);
        Assert.IsType<PackageChainItem>(model.Chain[1]);
        Assert.IsType<RollbackBoundaryChainItem>(model.Chain[2]);
        Assert.IsType<PackageChainItem>(model.Chain[3]);
    }

    [Fact]
    public void Chain_WithBoundary_StillPopulatesPackages()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .RollbackBoundary("RB1")
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        // Packages list should still be populated for backwards compatibility
        Assert.Single(model.Packages);
        Assert.Equal("App", model.Packages[0].Id);
    }
}
