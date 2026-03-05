namespace FalkForge.Compiler.Bundle.Tests.Builders;

using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

public sealed class SearchConditionBuilderTests
{
    [Fact]
    public void Build_WithFileExistsSearch_SetsSearchCondition()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .SearchCondition(sc => sc.FileExists(@"C:\Program Files\App\app.exe"))))
            .Build();

        var pkg = model.Packages[0];
        Assert.Single(pkg.SearchConditions);
        Assert.Equal(SearchConditionType.FileExists, pkg.SearchConditions[0].Type);
        Assert.Equal(@"C:\Program Files\App\app.exe", pkg.SearchConditions[0].Path);
    }

    [Fact]
    public void Build_WithFileVersionSearch_SetsVersionComparison()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .SearchCondition(sc => sc.FileVersion(@"C:\Program Files\App\app.exe", ">=", "2.0.0"))))
            .Build();

        var pkg = model.Packages[0];
        Assert.Single(pkg.SearchConditions);
        Assert.Equal(SearchConditionType.FileVersion, pkg.SearchConditions[0].Type);
        Assert.Equal(@"C:\Program Files\App\app.exe", pkg.SearchConditions[0].Path);
        Assert.Equal(">=", pkg.SearchConditions[0].Comparison);
        Assert.Equal("2.0.0", pkg.SearchConditions[0].Value);
    }

    [Fact]
    public void Build_WithDetectionMode_SetsMode()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .DetectionMode(DetectionMode.SearchOnly)))
            .Build();

        Assert.Equal(DetectionMode.SearchOnly, model.Packages[0].DetectionMode);
    }

    [Fact]
    public void Build_MultipleSearchConditions_PreservesAll()
    {
        var model = new BundleBuilder()
            .Name("TestBundle")
            .Manufacturer("TestCo")
            .Chain(c => c.MsiPackage("app.msi", p => p
                .Id("App")
                .DetectionMode(DetectionMode.Combined)
                .SearchCondition(sc => sc.FileExists(@"C:\Program Files\App\app.exe"))
                .SearchCondition(sc => sc.DirectoryExists(@"C:\Program Files\App\Data"))
                .SearchCondition(sc => sc.FileVersion(@"C:\Program Files\App\app.exe", ">=", "1.0.0"))))
            .Build();

        var pkg = model.Packages[0];
        Assert.Equal(3, pkg.SearchConditions.Count);
        Assert.Equal(SearchConditionType.FileExists, pkg.SearchConditions[0].Type);
        Assert.Equal(SearchConditionType.DirectoryExists, pkg.SearchConditions[1].Type);
        Assert.Equal(SearchConditionType.FileVersion, pkg.SearchConditions[2].Type);
        Assert.Equal(DetectionMode.Combined, pkg.DetectionMode);
    }
}
