using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class BundleBuilderPreUITests
{
    [Fact]
    public void BundleBuilder_PreUIPrerequisite_AddsToModel()
    {
        var model = CreateMinimalBundleBuilder()
            .PreUIPrerequisite("dotnet-runtime-10.0-win-x64.exe", p => p
                .Id("DotNet10Desktop")
                .DisplayName(".NET 10 Desktop Runtime (x64)")
                .Arguments("/quiet /norestart")
                .SearchCondition(sc => sc.RegistryValue(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\dotnet\Setup\InstalledVersions\x64",
                    "10.0.0",
                    "=",
                    "10.0.0")))
            .Build();

        Assert.Single(model.PreUIPackages);
        Assert.Equal("DotNet10Desktop", model.PreUIPackages[0].Id);
        Assert.Equal(".NET 10 Desktop Runtime (x64)", model.PreUIPackages[0].DisplayName);
        Assert.Equal("/quiet /norestart", model.PreUIPackages[0].Arguments);
    }

    [Fact]
    public void BundleBuilder_MultiplePreUIPrerequisites_AllAddedToModel()
    {
        var model = CreateMinimalBundleBuilder()
            .PreUIPrerequisite("dotnet-runtime-10.0-win-x64.exe", p => p
                .Id("DotNet10Desktop")
                .DisplayName(".NET 10 Desktop Runtime")
                .Arguments("/quiet"))
            .PreUIPrerequisite("vcredist_x64.exe", p => p
                .Id("VCRedist14x64")
                .DisplayName("Visual C++ 2022 Redistributable (x64)")
                .Arguments("/quiet /norestart"))
            .Build();

        Assert.Equal(2, model.PreUIPackages.Count);
        Assert.Equal("DotNet10Desktop", model.PreUIPackages[0].Id);
        Assert.Equal("VCRedist14x64", model.PreUIPackages[1].Id);
    }

    [Fact]
    public void BundleBuilder_NoPreUIPrerequisites_ModelHasEmptyList()
    {
        var model = CreateMinimalBundleBuilder().Build();

        Assert.Empty(model.PreUIPackages);
    }

    private static BundleBuilder CreateMinimalBundleBuilder()
    {
        return new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Version("1.0.0")
            .Chain(c => c.ExePackage("stub.exe", p => p
                .Id("StubPkg")
                .DisplayName("Stub")));
    }
}
