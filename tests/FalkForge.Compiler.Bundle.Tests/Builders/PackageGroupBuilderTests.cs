using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Builders;

public sealed class PackageGroupBuilderTests
{
    [Fact]
    public void Build_SetsId()
    {
        var builder = new PackageGroupBuilder();
        var group = builder.Id("NetFx472").Build();

        Assert.Equal("NetFx472", group.Id);
    }

    [Fact]
    public void Build_WithNoPackages_ReturnsEmptyList()
    {
        var group = new PackageGroupBuilder()
            .Id("EmptyGroup")
            .Build();

        Assert.Empty(group.Packages);
    }

    [Fact]
    public void ExePackage_AddsPackageWithCorrectType()
    {
        var group = new PackageGroupBuilder()
            .Id("TestGroup")
            .ExePackage("setup.exe", p => p
                .Id("Setup1")
                .DisplayName("Setup"))
            .Build();

        Assert.Single(group.Packages);
        Assert.Equal("Setup1", group.Packages[0].Id);
        Assert.Equal(BundlePackageType.ExePackage, group.Packages[0].Type);
    }

    [Fact]
    public void MsiPackage_AddsPackageWithCorrectType()
    {
        var group = new PackageGroupBuilder()
            .Id("TestGroup")
            .MsiPackage("app.msi", p => p
                .Id("App1")
                .DisplayName("App"))
            .Build();

        Assert.Single(group.Packages);
        Assert.Equal("App1", group.Packages[0].Id);
        Assert.Equal(BundlePackageType.MsiPackage, group.Packages[0].Type);
    }

    [Fact]
    public void MultiplePackages_PreservesOrder()
    {
        var group = new PackageGroupBuilder()
            .Id("TestGroup")
            .ExePackage("first.exe", p => p.Id("First"))
            .MsiPackage("second.msi", p => p.Id("Second"))
            .ExePackage("third.exe", p => p.Id("Third"))
            .Build();

        Assert.Equal(3, group.Packages.Count);
        Assert.Equal("First", group.Packages[0].Id);
        Assert.Equal("Second", group.Packages[1].Id);
        Assert.Equal("Third", group.Packages[2].Id);
    }

    [Fact]
    public void ExePackage_PassesConfigurationToBuilder()
    {
        var group = new PackageGroupBuilder()
            .Id("TestGroup")
            .ExePackage("setup.exe", p => p
                .Id("Setup1")
                .DisplayName("My Setup")
                .Vital(true)
                .Prerequisite()
                .SearchCondition(sc => sc.RegistryValue(
                    RegistryRoot.LocalMachine,
                    @"SOFTWARE\Test",
                    "Version",
                    ">=",
                    "1.0")))
            .Build();

        var pkg = group.Packages[0];
        Assert.Equal("My Setup", pkg.DisplayName);
        Assert.True(pkg.Vital);
        Assert.True(pkg.IsPrerequisite);
        Assert.Single(pkg.SearchConditions);
        Assert.Equal(SearchConditionType.RegistryValue, pkg.SearchConditions[0].Type);
    }

    [Fact]
    public void ChainBuilder_PackageGroup_FlattensIntoChain()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .PackageGroup(g => g
                    .Id("Prereqs")
                    .ExePackage("prereq.exe", p => p.Id("Prereq1"))
                    .MsiPackage("prereq.msi", p => p.Id("Prereq2")))
                .MsiPackage("app.msi", p => p.Id("MainApp")))
            .Build();

        // Package group packages should be flattened into the main packages list
        Assert.Equal(3, model.Packages.Count);
        Assert.Equal("Prereq1", model.Packages[0].Id);
        Assert.Equal("Prereq2", model.Packages[1].Id);
        Assert.Equal("MainApp", model.Packages[2].Id);
    }

    [Fact]
    public void ChainBuilder_PackageGroup_FlattensIntoChainItems()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .PackageGroup(g => g
                    .Id("Prereqs")
                    .ExePackage("prereq.exe", p => p.Id("Prereq1")))
                .MsiPackage("app.msi", p => p.Id("MainApp")))
            .Build();

        // Chain items should contain flattened packages in order
        Assert.Equal(2, model.Chain.Count);
        var first = Assert.IsType<PackageChainItem>(model.Chain[0]);
        var second = Assert.IsType<PackageChainItem>(model.Chain[1]);
        Assert.Equal("Prereq1", first.Package.Id);
        Assert.Equal("MainApp", second.Package.Id);
    }

    [Fact]
    public void ChainBuilder_PackageGroupFromModel_FlattensIntoChain()
    {
        var prereqGroup = new PackageGroupBuilder()
            .Id("Prereqs")
            .ExePackage("prereq.exe", p => p.Id("Prereq1").Prerequisite())
            .Build();

        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .PackageGroup(prereqGroup)
                .MsiPackage("app.msi", p => p.Id("MainApp")))
            .Build();

        Assert.Equal(2, model.Packages.Count);
        Assert.Equal("Prereq1", model.Packages[0].Id);
        Assert.True(model.Packages[0].IsPrerequisite);
    }

    [Fact]
    public void ChainBuilder_MultiplePackageGroups_PreservesOrder()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c
                .PackageGroup(g => g
                    .Id("Group1")
                    .ExePackage("a.exe", p => p.Id("A")))
                .PackageGroup(g => g
                    .Id("Group2")
                    .ExePackage("b.exe", p => p.Id("B")))
                .MsiPackage("c.msi", p => p.Id("C")))
            .Build();

        Assert.Equal(3, model.Packages.Count);
        Assert.Equal("A", model.Packages[0].Id);
        Assert.Equal("B", model.Packages[1].Id);
        Assert.Equal("C", model.Packages[2].Id);
    }
}
