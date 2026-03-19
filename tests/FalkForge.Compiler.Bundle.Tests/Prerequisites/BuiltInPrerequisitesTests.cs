using FalkForge.Compiler.Bundle.Prerequisites;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Prerequisites;

public sealed class BuiltInPrerequisitesTests
{
    [Fact]
    public void NetFx472_ReturnsValidGroup()
    {
        var group = BuiltInPrerequisites.NetFx472();

        Assert.Equal("NetFx472", group.Id);
        Assert.Single(group.Packages);

        var pkg = group.Packages[0];
        Assert.Equal(BundlePackageType.ExePackage, pkg.Type);
        Assert.True(pkg.Vital);
        Assert.True(pkg.IsPrerequisite);
        Assert.Contains("/q", pkg.Properties["InstallArguments"]);
        Assert.Contains("/norestart", pkg.Properties["InstallArguments"]);
    }

    [Fact]
    public void NetFx472_HasRegistryDetection()
    {
        var group = BuiltInPrerequisites.NetFx472();
        var pkg = group.Packages[0];

        Assert.NotEmpty(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Contains("NET Framework Setup", condition.Path);
        Assert.Equal("Release", condition.Value);
        Assert.Equal(">=:461808", condition.Comparison);
    }

    [Fact]
    public void VCRedist14x64_ReturnsValidGroup()
    {
        var group = BuiltInPrerequisites.VCRedist14x64();

        Assert.Equal("VCRedist14x64", group.Id);
        Assert.Single(group.Packages);

        var pkg = group.Packages[0];
        Assert.Equal(BundlePackageType.ExePackage, pkg.Type);
        Assert.True(pkg.Vital);
        Assert.True(pkg.IsPrerequisite);
        Assert.Contains("/install", pkg.Properties["InstallArguments"]);
        Assert.Contains("/quiet", pkg.Properties["InstallArguments"]);
    }

    [Fact]
    public void VCRedist14x64_HasRegistryDetection()
    {
        var group = BuiltInPrerequisites.VCRedist14x64();
        var pkg = group.Packages[0];

        Assert.NotEmpty(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Contains(@"VC\Runtimes\x64", condition.Path);
        Assert.Equal("Installed", condition.Value);
        Assert.Equal("=:1", condition.Comparison);
    }

    [Fact]
    public void OdbcDriver17_ReturnsValidGroup()
    {
        var group = BuiltInPrerequisites.OdbcDriver17();

        Assert.Equal("OdbcDriver17", group.Id);
        Assert.Single(group.Packages);

        var pkg = group.Packages[0];
        Assert.Equal(BundlePackageType.MsiPackage, pkg.Type);
        Assert.True(pkg.Vital);
        Assert.True(pkg.IsPrerequisite);
    }

    [Fact]
    public void OdbcDriver17_HasRegistryDetection()
    {
        var group = BuiltInPrerequisites.OdbcDriver17();
        var pkg = group.Packages[0];

        Assert.NotEmpty(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Contains("ODBC Driver 17", condition.Path);
        Assert.Equal("Driver", condition.Value);
        Assert.Equal("exists", condition.Comparison);
    }

    [Fact]
    public void SqlExpress2017_ReturnsValidGroup()
    {
        var group = BuiltInPrerequisites.SqlExpress2017();

        Assert.Equal("SqlExpress2017", group.Id);
        Assert.Single(group.Packages);

        var pkg = group.Packages[0];
        Assert.Equal(BundlePackageType.ExePackage, pkg.Type);
        Assert.True(pkg.Vital);
        Assert.True(pkg.IsPrerequisite);
        Assert.Contains("/IACCEPTSQLSERVERLICENSETERMS", pkg.Properties["InstallArguments"]);
    }

    [Fact]
    public void SqlExpress2017_HasRegistryDetection()
    {
        var group = BuiltInPrerequisites.SqlExpress2017();
        var pkg = group.Packages[0];

        Assert.NotEmpty(pkg.SearchConditions);
        var condition = pkg.SearchConditions[0];
        Assert.Equal(SearchConditionType.RegistryValue, condition.Type);
        Assert.Contains("Microsoft SQL Server", condition.Path);
        Assert.Equal("SQLEXPRESS", condition.Value);
        Assert.Equal("exists", condition.Comparison);
    }

    [Fact]
    public void AllPrerequisites_HaveSearchOnlyDetectionMode()
    {
        var groups = new[]
        {
            BuiltInPrerequisites.NetFx472(),
            BuiltInPrerequisites.VCRedist14x64(),
            BuiltInPrerequisites.OdbcDriver17(),
            BuiltInPrerequisites.SqlExpress2017()
        };

        foreach (var group in groups)
        foreach (var pkg in group.Packages)
            Assert.Equal(DetectionMode.SearchOnly, pkg.DetectionMode);
    }

    [Fact]
    public void AllPrerequisites_HaveNonEmptySourcePath()
    {
        var groups = new[]
        {
            BuiltInPrerequisites.NetFx472(),
            BuiltInPrerequisites.VCRedist14x64(),
            BuiltInPrerequisites.OdbcDriver17(),
            BuiltInPrerequisites.SqlExpress2017()
        };

        foreach (var group in groups)
        foreach (var pkg in group.Packages)
            Assert.False(string.IsNullOrWhiteSpace(pkg.SourcePath));
    }

    [Fact]
    public void AllPrerequisites_HaveDisplayNames()
    {
        var groups = new[]
        {
            BuiltInPrerequisites.NetFx472(),
            BuiltInPrerequisites.VCRedist14x64(),
            BuiltInPrerequisites.OdbcDriver17(),
            BuiltInPrerequisites.SqlExpress2017()
        };

        foreach (var group in groups)
        foreach (var pkg in group.Packages)
            Assert.False(string.IsNullOrWhiteSpace(pkg.DisplayName));
    }
}
