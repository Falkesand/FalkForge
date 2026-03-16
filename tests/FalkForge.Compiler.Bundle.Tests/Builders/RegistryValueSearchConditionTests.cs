using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class RegistryValueSearchConditionTests
{
    [Fact]
    public void RegistryValue_SetsTypePathAndComparison()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.ExePackage("setup.exe", p => p
                .Id("Setup")
                .SearchCondition(sc => sc.RegistryValue(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Test\Key",
                    "Release",
                    ">=",
                    "42"))))
            .Build();

        var pkg = model.Packages[0];
        Assert.Single(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Equal(@"HKLM\SOFTWARE\Test\Key", condition.Path);
        Assert.Equal("Release", condition.Value);
        Assert.Equal(">=:42", condition.Comparison);
    }

    [Fact]
    public void RegistryExists_SetsExistsComparison()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.ExePackage("setup.exe", p => p
                .Id("Setup")
                .SearchCondition(sc => sc.RegistryExists(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Test\Key",
                    "ValueName"))))
            .Build();

        var pkg = model.Packages[0];
        Assert.Single(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Equal(@"HKLM\SOFTWARE\Test\Key", condition.Path);
        Assert.Equal("ValueName", condition.Value);
        Assert.Equal("exists", condition.Comparison);
    }

    [Fact]
    public void RegistryExists_WithoutValueName_SetsNullValue()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.ExePackage("setup.exe", p => p
                .Id("Setup")
                .SearchCondition(sc => sc.RegistryExists(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Test\Key"))))
            .Build();

        var pkg = model.Packages[0];
        var condition = pkg.SearchConditions[0];
        Assert.Null(condition.Value);
        Assert.Equal("exists", condition.Comparison);
    }
}
