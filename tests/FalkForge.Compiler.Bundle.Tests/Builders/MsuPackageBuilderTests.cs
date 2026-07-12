using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class MsuPackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToMsuPackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MsuPackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("KB12345")))
            .Build();

        Assert.Equal("KB12345", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("KB12345")
                .DisplayName("Security Update KB12345")))
            .Build();

        Assert.Equal("Security Update KB12345", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsKbArticleCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("TestMsu")
                .KbArticle("12345")))
            .Build();

        Assert.Equal("12345", model.Packages[0].KbArticle);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("TestMsu")
                .Vital(false)))
            .Build();

        Assert.False(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_DefaultVitalIsTrue()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_SetsSourcePathCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage(@"C:\updates\KB12345.msu", p => p.Id("TestMsu")))
            .Build();

        Assert.Equal(@"C:\updates\KB12345.msu", model.Packages[0].SourcePath);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("KB12345.msu", _ => { }))
            .Build();

        Assert.Equal("KB12345", model.Packages[0].Id);
    }

    // --- D4: chain knobs lifted from BundlePackageBuilder — BundlePackageModel is type-agnostic,
    // so the runtime honors these regardless of package type; only the fluent surface was missing. ---

    [Fact]
    public void Build_SetsLiftedChainOptions_ContainerRemotePayloadExitCodeDetectionSearchConditionPermanent()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsuPackage("update.msu", p => p
                .Id("TestMsu")
                .Container("MsuContainer")
                .RemotePayload("https://example.com/update.msu", "ABCDEF", 4096)
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)
                .DetectionMode(DetectionMode.SearchOnly)
                .SearchCondition(sc => sc.FileExists(@"C:\Windows\System32\update.dll"))
                .Permanent()))
            .Build();

        var pkg = model.Packages[0];
        Assert.Equal("MsuContainer", pkg.ContainerId);
        Assert.NotNull(pkg.RemotePayload);
        Assert.Equal("https://example.com/update.msu", pkg.RemotePayload!.DownloadUrl);
        Assert.Equal("ABCDEF", pkg.RemotePayload.Sha256Hash);
        Assert.Equal(4096, pkg.RemotePayload.Size);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, pkg.ExitCodes[3010]);
        Assert.Equal(DetectionMode.SearchOnly, pkg.DetectionMode);
        Assert.Single(pkg.SearchConditions);
        Assert.True(pkg.Permanent);
    }
}
