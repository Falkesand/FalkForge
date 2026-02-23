using FalkForge.Compiler.Bundle.Builders;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleBuilderReproducibleTests
{
    [Fact]
    public void Reproducible_BundleId_IsDeterministic()
    {
        var model1 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        var model2 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        Assert.NotEqual(Guid.Empty, model1.BundleId);
        Assert.Equal(model1.BundleId, model2.BundleId);
    }

    [Fact]
    public void Reproducible_UpgradeCode_IsDeterministic()
    {
        var model1 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        var model2 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        Assert.NotEqual(Guid.Empty, model1.UpgradeCode);
        Assert.Equal(model1.UpgradeCode, model2.UpgradeCode);
    }

    [Fact]
    public void Reproducible_BundleId_DiffersForDifferentVersion()
    {
        var modelV1 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        var modelV2 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("2.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        Assert.NotEqual(modelV1.BundleId, modelV2.BundleId);
    }

    [Fact]
    public void Reproducible_UpgradeCode_SameAcrossVersions()
    {
        var modelV1 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        var modelV2 = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("2.0.0")
            .Reproducible(1_700_000_000L)
            .Build();

        Assert.Equal(modelV1.UpgradeCode, modelV2.UpgradeCode);
    }

    [Fact]
    public void ExplicitBundleId_TakesPrecedenceOverDeterministic()
    {
        var explicitId = Guid.NewGuid();

        var model = new BundleBuilder()
            .Name("MyBundle")
            .Manufacturer("Acme Corp")
            .Version("1.0.0")
            .BundleId(explicitId)
            .Reproducible(1_700_000_000L)
            .Build();

        Assert.Equal(explicitId, model.BundleId);
    }
}
