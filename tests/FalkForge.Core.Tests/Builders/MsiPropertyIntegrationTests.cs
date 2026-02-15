using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class MsiPropertyIntegrationTests
{
    [Fact]
    public void Require_WithCondition_AddsLaunchCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Require(Condition.IsWindows10OrLater, "Requires Windows 10+");
        });

        var condition = package.LaunchConditions.Single();
        Assert.Equal("VersionNT >= 603", condition.Condition);
        Assert.Equal("Requires Windows 10+", condition.Message);
    }

    [Fact]
    public void Require_WithComposedCondition_AddsCorrectExpression()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Require(Condition.Is64BitOS & Condition.IsPrivileged, "64-bit admin required");
        });

        var condition = package.LaunchConditions.Single();
        Assert.Equal("(VersionNT64 OR Msix64) AND (Privileged)", condition.Condition);
    }

    [Fact]
    public void Require_WithMsiPropertyComparison_AddsCorrectExpression()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Require(MsiProperty.VersionNT >= 603, "Requires Windows 10+");
        });

        var condition = package.LaunchConditions.Single();
        Assert.Equal("VersionNT >= 603", condition.Condition);
    }

    [Fact]
    public void FeatureCondition_WithConditionType_AddsCorrectCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Feature("ServerFeature", f =>
            {
                f.Title = "Server";
                f.Condition(Condition.IsPrivileged, 1);
            });
        });

        var featureCondition = package.Features.Single().Conditions.Single();
        Assert.Equal("Privileged", featureCondition.Condition);
        Assert.Equal(1, featureCondition.Level);
    }

    [Fact]
    public void FeatureCondition_WithConditionDefaultLevel_UsesLevelZero()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Feature("ServerFeature", f =>
            {
                f.Title = "Server";
                f.Condition(Condition.IsPrivileged);
            });
        });

        var featureCondition = package.Features.Single().Conditions.Single();
        Assert.Equal("Privileged", featureCondition.Condition);
        Assert.Equal(0, featureCondition.Level);
    }

    [Fact]
    public void RegistryValue_WithMsiProperty_FormatsCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Registry(r => r
                .Key(RegistryRoot.LocalMachine, @"Software\Test", k => k
                    .Value("InstallPath", MsiProperty.InstallFolder)));
        });

        var entry = package.RegistryEntries.Single();
        Assert.Equal("InstallPath", entry.ValueName);
        Assert.Equal("[INSTALLFOLDER]", entry.Value);
    }

    [Fact]
    public void RegistryValue_WithMsiPropertySlash_FormatsWithSubPath()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Registry(r => r
                .Key(RegistryRoot.LocalMachine, @"Software\Test", k => k
                    .Value("BinPath", MsiProperty.InstallFolder / "bin")));
        });

        var entry = package.RegistryEntries.Single();
        Assert.Equal("[INSTALLFOLDER]bin", entry.Value);
    }

    [Fact]
    public void EnvironmentVariable_WithMsiProperty_FormatsCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.EnvironmentVariable("APP_HOME", MsiProperty.InstallFolder, ev =>
            {
                ev.IsSystem = true;
                ev.Action = EnvironmentVariableAction.Set;
            });
        });

        var envVar = package.EnvironmentVariables.Single();
        Assert.Equal("APP_HOME", envVar.Name);
        Assert.Equal("[INSTALLFOLDER]", envVar.Value);
    }

    [Fact]
    public void Require_StringOverload_StillWorks()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Require("VersionNT >= 603", "Requires Windows 10+");
        });

        var condition = package.LaunchConditions.Single();
        Assert.Equal("VersionNT >= 603", condition.Condition);
    }

    [Fact]
    public void Require_ConditionImplicitString_WorksWithStringOverload()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Require((string)Condition.IsWindows10OrLater, "Requires Windows 10+");
        });

        var condition = package.LaunchConditions.Single();
        Assert.Equal("VersionNT >= 603", condition.Condition);
    }
}
