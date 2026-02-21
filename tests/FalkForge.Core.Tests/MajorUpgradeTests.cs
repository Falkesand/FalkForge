using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class MajorUpgradeTests
{
    [Fact]
    public void MajorUpgrade_DefaultValues_ProducesExpectedModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
        });

        Assert.NotNull(package.MajorUpgrade);
        Assert.False(package.MajorUpgrade.AllowSameVersionUpgrades);
        Assert.Equal(RemoveExistingProductsSchedule.AfterInstallValidate, package.MajorUpgrade.Schedule);
        Assert.True(package.MajorUpgrade.MigrateFeatures);
    }

    [Fact]
    public void AllowSameVersionUpgrades_SetsFlag()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu.AllowSameVersionUpgrades());
        });

        Assert.True(package.MajorUpgrade!.AllowSameVersionUpgrades);
    }

    [Fact]
    public void Schedule_SetsScheduleValue()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .Schedule(RemoveExistingProductsSchedule.AfterInstallFinalize));
        });

        Assert.Equal(RemoveExistingProductsSchedule.AfterInstallFinalize, package.MajorUpgrade!.Schedule);
    }

    [Fact]
    public void MigrateFeatures_SetsFalse()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu.MigrateFeatures(false));
        });

        Assert.False(package.MajorUpgrade!.MigrateFeatures);
    }

    [Fact]
    public void FluentChaining_AllMethods_ProducesCorrectModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .AllowSameVersionUpgrades()
                .Schedule(RemoveExistingProductsSchedule.AfterInstallExecute)
                .MigrateFeatures(false));
        });

        var mu = package.MajorUpgrade!;
        Assert.True(mu.AllowSameVersionUpgrades);
        Assert.Equal(RemoveExistingProductsSchedule.AfterInstallExecute, mu.Schedule);
        Assert.False(mu.MigrateFeatures);
    }

    [Fact]
    public void PackageBuilder_MajorUpgrade_SetsModelOnPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .Schedule(RemoveExistingProductsSchedule.AfterInstallInitialize));
        });

        Assert.NotNull(package.MajorUpgrade);
        Assert.Equal(RemoveExistingProductsSchedule.AfterInstallInitialize, package.MajorUpgrade.Schedule);
    }

    [Fact]
    public void Validation_MajorUpgradeWithEmptyUpgradeCode_ProducesMUP001Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.Empty,
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MUP001");
    }

    [Fact]
    public void Validation_BothUpgradeAndMajorUpgrade_ProducesMUP003Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Upgrade = new UpgradeModel(),
            MajorUpgrade = new MajorUpgradeModel(),
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MUP003");
    }

    [Fact]
    public void Validation_OnlyMajorUpgrade_NoMUP003Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code == "MUP003");
    }
}
