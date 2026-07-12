using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class MspPackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToMspPackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("TestMsp")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.MspPackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("Hotfix1")))
            .Build();

        Assert.Equal("Hotfix1", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsPatchCodeCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .PatchCode("{PATCH-GUID}")))
            .Build();

        Assert.Equal("{PATCH-GUID}", model.Packages[0].PatchCode);
    }

    [Fact]
    public void Build_SetsTargetProductCodeCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .TargetProductCode("{PRODUCT-GUID}")))
            .Build();

        Assert.Equal("{PRODUCT-GUID}", model.Packages[0].TargetProductCode);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .DisplayName("Critical Hotfix")))
            .Build();

        Assert.Equal("Critical Hotfix", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
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
            .Chain(c => c.MspPackage("hotfix.msp", p => p.Id("TestMsp")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("critical-hotfix.msp", _ => { }))
            .Build();

        Assert.Equal("critical-hotfix", model.Packages[0].Id);
    }

    // --- D4: chain knobs lifted from BundlePackageBuilder — BundlePackageModel is type-agnostic,
    // so the runtime honors these regardless of package type; only the fluent surface was missing. ---

    [Fact]
    public void Build_SetsLiftedChainOptions_ContainerRemotePayloadExitCodeDetectionSearchConditionPermanent()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MspPackage("hotfix.msp", p => p
                .Id("TestMsp")
                .Container("MspContainer")
                .RemotePayload("https://example.com/hotfix.msp", "FEDCBA", 2048)
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)
                .DetectionMode(DetectionMode.Combined)
                .SearchCondition(sc => sc.FileExists(@"C:\Windows\System32\hotfix.dll"))
                .Permanent()))
            .Build();

        var pkg = model.Packages[0];
        Assert.Equal("MspContainer", pkg.ContainerId);
        Assert.NotNull(pkg.RemotePayload);
        Assert.Equal("https://example.com/hotfix.msp", pkg.RemotePayload!.DownloadUrl);
        Assert.Equal("FEDCBA", pkg.RemotePayload.Sha256Hash);
        Assert.Equal(2048, pkg.RemotePayload.Size);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, pkg.ExitCodes[3010]);
        Assert.Equal(DetectionMode.Combined, pkg.DetectionMode);
        Assert.Single(pkg.SearchConditions);
        Assert.True(pkg.Permanent);
    }
}
