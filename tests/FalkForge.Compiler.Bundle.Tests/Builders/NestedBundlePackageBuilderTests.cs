using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class NestedBundlePackageBuilderTests
{
    [Fact]
    public void Build_SetsTypeToBundlePackage()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Single(model.Packages);
        Assert.Equal(BundlePackageType.BundlePackage, model.Packages[0].Type);
    }

    [Fact]
    public void Build_SetsIdCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Equal("NestedBundle", model.Packages[0].Id);
    }

    [Fact]
    public void Build_SetsDisplayNameCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p
                .Id("NestedBundle")
                .DisplayName("Nested Installer")))
            .Build();

        Assert.Equal("Nested Installer", model.Packages[0].DisplayName);
    }

    [Fact]
    public void Build_SetsVitalCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p
                .Id("NestedBundle")
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
            .Chain(c => c.BundlePackage("nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.True(model.Packages[0].Vital);
    }

    [Fact]
    public void Build_SetsSourcePathCorrectly()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage(@"C:\bundles\nested.exe", p => p.Id("NestedBundle")))
            .Build();

        Assert.Equal(@"C:\bundles\nested.exe", model.Packages[0].SourcePath);
    }

    [Fact]
    public void Build_DefaultId_IsFileNameWithoutExtension()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested-installer.exe", _ => { }))
            .Build();

        Assert.Equal("nested-installer", model.Packages[0].Id);
    }

    // --- D4: chain knobs lifted from BundlePackageBuilder — BundlePackageModel is type-agnostic,
    // so the runtime honors these regardless of package type; only the fluent surface was missing. ---

    [Fact]
    public void Build_SetsLiftedChainOptions_ContainerRemotePayloadExitCodeDetectionSearchConditionPermanent()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.BundlePackage("nested.exe", p => p
                .Id("NestedBundle")
                .Container("NestedContainer")
                .RemotePayload("https://example.com/nested.exe", "112233", 8192)
                .ExitCode(3010, ExitCodeBehavior.ScheduleReboot)
                .DetectionMode(DetectionMode.SearchOnly)
                .SearchCondition(sc => sc.FileExists(@"C:\Windows\System32\nested.dll"))
                .Permanent()))
            .Build();

        var pkg = model.Packages[0];
        Assert.Equal("NestedContainer", pkg.ContainerId);
        Assert.NotNull(pkg.RemotePayload);
        Assert.Equal("https://example.com/nested.exe", pkg.RemotePayload!.DownloadUrl);
        Assert.Equal("112233", pkg.RemotePayload.Sha256Hash);
        Assert.Equal(8192, pkg.RemotePayload.Size);
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, pkg.ExitCodes[3010]);
        Assert.Equal(DetectionMode.SearchOnly, pkg.DetectionMode);
        Assert.Single(pkg.SearchConditions);
        Assert.True(pkg.Permanent);
    }
}
