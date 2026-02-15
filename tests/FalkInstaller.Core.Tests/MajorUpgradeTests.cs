using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class MajorUpgradeTests
{
    [Fact]
    public void MajorUpgrade_DefaultValues_ProducesExpectedModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .DowngradeErrorMessage("No downgrade."));
        });

        Assert.NotNull(package.MajorUpgrade);
        Assert.False(package.MajorUpgrade.AllowDowngrades);
        Assert.False(package.MajorUpgrade.AllowSameVersionUpgrades);
        Assert.Equal("No downgrade.", package.MajorUpgrade.DowngradeErrorMessage);
        Assert.Equal(RemoveExistingProductsSchedule.AfterInstallValidate, package.MajorUpgrade.Schedule);
        Assert.True(package.MajorUpgrade.MigrateFeatures);
    }

    [Fact]
    public void AllowDowngrades_SetsFlag()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu.AllowDowngrades());
        });

        Assert.True(package.MajorUpgrade!.AllowDowngrades);
    }

    [Fact]
    public void AllowSameVersionUpgrades_SetsFlag()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .AllowSameVersionUpgrades()
                .DowngradeErrorMessage("No downgrade."));
        });

        Assert.True(package.MajorUpgrade!.AllowSameVersionUpgrades);
    }

    [Fact]
    public void DowngradeErrorMessage_SetsMessage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .DowngradeErrorMessage("Cannot downgrade this product."));
        });

        Assert.Equal("Cannot downgrade this product.", package.MajorUpgrade!.DowngradeErrorMessage);
    }

    [Fact]
    public void Schedule_SetsScheduleValue()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu
                .DowngradeErrorMessage("No downgrade.")
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
            p.MajorUpgrade(mu => mu
                .DowngradeErrorMessage("No downgrade.")
                .MigrateFeatures(false));
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
                .AllowDowngrades()
                .AllowSameVersionUpgrades()
                .DowngradeErrorMessage("Blocked")
                .Schedule(RemoveExistingProductsSchedule.AfterInstallExecute)
                .MigrateFeatures(false));
        });

        var mu = package.MajorUpgrade!;
        Assert.True(mu.AllowDowngrades);
        Assert.True(mu.AllowSameVersionUpgrades);
        Assert.Equal("Blocked", mu.DowngradeErrorMessage);
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
                .DowngradeErrorMessage("No downgrade allowed.")
                .Schedule(RemoveExistingProductsSchedule.AfterInstallInitialize));
        });

        Assert.NotNull(package.MajorUpgrade);
        Assert.Equal("No downgrade allowed.", package.MajorUpgrade.DowngradeErrorMessage);
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
            MajorUpgrade = new MajorUpgradeModel
            {
                DowngradeErrorMessage = "Cannot downgrade."
            },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MUP001");
    }

    [Fact]
    public void Validation_MajorUpgradeWithoutDowngradeMessage_WhenDowngradesNotAllowed_ProducesMUP002Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { }); // No DowngradeErrorMessage, AllowDowngrades defaults to false
        });

        var result = ModelValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MUP002");
    }

    [Fact]
    public void Validation_MajorUpgradeWithDowngradesAllowed_NoMessageRequired()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => mu.AllowDowngrades());
        });

        var result = ModelValidator.Validate(package);

        // MUP002 should not be present since downgrades are allowed
        Assert.DoesNotContain(result.Errors, e => e.Code == "MUP002");
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
            MajorUpgrade = new MajorUpgradeModel
            {
                DowngradeErrorMessage = "Cannot downgrade."
            },
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
            MajorUpgrade = new MajorUpgradeModel
            {
                DowngradeErrorMessage = "Cannot downgrade."
            },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code == "MUP003");
    }
}
