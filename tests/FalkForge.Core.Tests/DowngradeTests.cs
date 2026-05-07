using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class DowngradeTests
{
    // -------------------------------------------------------------------------
    // DowngradeBuilder — unit behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public void DefaultDowngradeBuilder_ProducesAllowDowngradesFalse_AndNullMessage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => { });
        });

        Assert.NotNull(package.Downgrade);
        Assert.False(package.Downgrade.AllowDowngrades);
        Assert.Null(package.Downgrade.ErrorMessage);
    }

    [Fact]
    public void Allow_SetsAllowDowngradesTrue_AndNullMessage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Allow());
        });

        Assert.NotNull(package.Downgrade);
        Assert.True(package.Downgrade.AllowDowngrades);
        Assert.Null(package.Downgrade.ErrorMessage);
    }

    [Fact]
    public void Block_SetsAllowDowngradesFalse_AndSetsErrorMessage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Block("Cannot downgrade this product."));
        });

        Assert.NotNull(package.Downgrade);
        Assert.False(package.Downgrade.AllowDowngrades);
        Assert.Equal("Cannot downgrade this product.", package.Downgrade.ErrorMessage);
    }

    [Fact]
    public void FluentChaining_AllowThenBlock_LastCallWins()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Allow().Block("Blocked."));
        });

        Assert.False(package.Downgrade!.AllowDowngrades);
        Assert.Equal("Blocked.", package.Downgrade.ErrorMessage);
    }

    [Fact]
    public void FluentChaining_BlockThenAllow_AllowDowngradesIsTrue()
    {
        // Allow() sets AllowDowngrades=true but does not clear the previously set message.
        // The authoritative outcome is AllowDowngrades=true; the message field is irrelevant
        // when AllowDowngrades is true because it is never surfaced to MSI.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Block("Blocked.").Allow());
        });

        Assert.True(package.Downgrade!.AllowDowngrades);
    }

    // -------------------------------------------------------------------------
    // PackageBuilder integration
    // -------------------------------------------------------------------------

    [Fact]
    public void PackageBuilder_Downgrade_SetsModelOnPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Block("No going back."));
        });

        Assert.NotNull(package.Downgrade);
        Assert.Equal("No going back.", package.Downgrade.ErrorMessage);
    }

    [Fact]
    public void PackageBuilder_WithoutDowngrade_PackageDowngradeIsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
        });

        Assert.Null(package.Downgrade);
    }

    // -------------------------------------------------------------------------
    // Validation — DNG001
    // -------------------------------------------------------------------------

    [Fact]
    public void Validation_DNG001_Block_WithEmptyMessage_Fails()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = false, ErrorMessage = "" },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "DNG001");
    }

    [Fact]
    public void Validation_DNG001_Block_WithWhitespaceMessage_Fails()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = false, ErrorMessage = "   " },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "DNG001");
    }

    [Fact]
    public void Validation_DNG001_Block_WithNullMessage_Fails()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = false, ErrorMessage = null },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "DNG001");
    }

    [Fact]
    public void Validation_DNG001_Allow_WithNoMessage_IsValid()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = true, ErrorMessage = null },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG001");
    }

    // -------------------------------------------------------------------------
    // Validation — DNG002
    // -------------------------------------------------------------------------

    [Fact]
    public void Validation_DNG002_Downgrade_WithoutMajorUpgrade_Fails()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Downgrade = new DowngradeModel { AllowDowngrades = true },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "DNG002");
    }

    [Fact]
    public void Validation_DNG002_Downgrade_WithMajorUpgrade_NoDNG002Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MajorUpgrade = new MajorUpgradeModel(),
            Downgrade = new DowngradeModel { AllowDowngrades = true },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = ModelValidator.Inspect(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG002");
    }

    // -------------------------------------------------------------------------
    // Validation — fully valid combinations via builder
    // -------------------------------------------------------------------------

    [Fact]
    public void Validation_DowngradeAllow_WithMajorUpgrade_ProducesNoDowngradeErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Allow());
        });

        var result = ModelValidator.Inspect(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG001");
        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG002");
    }

    [Fact]
    public void Validation_DowngradeBlock_WithMessageAndMajorUpgrade_ProducesNoDowngradeErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MajorUpgrade(mu => { });
            p.Downgrade(dg => dg.Block("Downgrade is not supported."));
        });

        var result = ModelValidator.Inspect(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG001");
        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "DNG002");
    }
}
