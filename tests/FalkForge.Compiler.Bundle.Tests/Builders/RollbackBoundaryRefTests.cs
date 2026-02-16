using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class RollbackBoundaryRefTests
{
    [Fact]
    public void DefineRollbackBoundary_ExplicitId_ReturnsRef()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var boundaryRef = builder.DefineRollbackBoundary("RB1");

        Assert.Equal("RB1", boundaryRef.Id);
    }

    [Fact]
    public void DefineRollbackBoundary_AutoId_Unique()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var ref1 = builder.DefineRollbackBoundary();
        var ref2 = builder.DefineRollbackBoundary();

        Assert.NotEqual(ref1.Id, ref2.Id);
    }

    [Fact]
    public void DefineRollbackBoundary_AutoId_CounterPattern()
    {
        var builder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var ref1 = builder.DefineRollbackBoundary();
        var ref2 = builder.DefineRollbackBoundary();

        Assert.Equal("RollbackBoundary_1", ref1.Id);
        Assert.Equal("RollbackBoundary_2", ref2.Id);
    }

    [Fact]
    public void ChainBuilder_RollbackBoundary_AcceptsRef()
    {
        var bundleBuilder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var boundaryRef = bundleBuilder.DefineRollbackBoundary("RB1");

        var model = bundleBuilder
            .Chain(c => c
                .RollbackBoundary(boundaryRef)
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        var boundary = model.Chain.OfType<RollbackBoundaryChainItem>().Single();
        Assert.Equal("RB1", boundary.Boundary.Id);
    }

    [Fact]
    public void ChainBuilder_RollbackBoundary_AcceptsRef_WithConfigure()
    {
        var bundleBuilder = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo");

        var boundaryRef = bundleBuilder.DefineRollbackBoundary("RB1");

        var model = bundleBuilder
            .Chain(c => c
                .RollbackBoundary(boundaryRef, rb => rb.Vital(false))
                .MsiPackage("app.msi", p => p.Id("App")))
            .Build();

        var boundary = model.Chain.OfType<RollbackBoundaryChainItem>().Single();
        Assert.Equal("RB1", boundary.Boundary.Id);
        Assert.False(boundary.Boundary.Vital);
    }

    [Fact]
    public void RollbackBoundaryRef_EqualityByValue()
    {
        var ref1 = new RollbackBoundaryRef("SameId");
        var ref2 = new RollbackBoundaryRef("SameId");

        Assert.Equal(ref1, ref2);
        Assert.True(ref1 == ref2);
    }

    [Fact]
    public void RollbackBoundaryRef_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RollbackBoundaryRef(null!));
    }

    [Fact]
    public void RollbackBoundaryRef_EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RollbackBoundaryRef(""));
    }
}
