using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class FeatureConditionTests
{
    [Fact]
    public void FeatureBuilder_Condition_AddsSingleCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Optional", f =>
            {
                f.Title = "Optional Feature";
                f.Condition("NOT REMOVE", 0);
            });
        });

        var feature = package.Features.First(f => f.Id == "Optional");
        Assert.Single(feature.Conditions);
        Assert.Equal("NOT REMOVE", feature.Conditions[0].Condition);
        Assert.Equal(0, feature.Conditions[0].Level);
    }

    [Fact]
    public void FeatureBuilder_Condition_AddsMultipleConditions()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Pro", f =>
            {
                f.Title = "Pro Feature";
                f.Condition("PREMIUM_LICENSE", 1);
                f.Condition("TRIAL_MODE", 1000);
                f.Condition("NOT REMOVE", 0);
            });
        });

        var feature = package.Features.First(f => f.Id == "Pro");
        Assert.Equal(3, feature.Conditions.Count);
        Assert.Equal("PREMIUM_LICENSE", feature.Conditions[0].Condition);
        Assert.Equal(1, feature.Conditions[0].Level);
        Assert.Equal("TRIAL_MODE", feature.Conditions[1].Condition);
        Assert.Equal(1000, feature.Conditions[1].Level);
        Assert.Equal("NOT REMOVE", feature.Conditions[2].Condition);
        Assert.Equal(0, feature.Conditions[2].Level);
    }

    [Fact]
    public void FeatureBuilder_Condition_DefaultLevel_IsZero()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Optional", f =>
            {
                f.Title = "Optional Feature";
                f.Condition("DISABLE_FEATURE");
            });
        });

        var feature = package.Features.First(f => f.Id == "Optional");
        Assert.Single(feature.Conditions);
        Assert.Equal("DISABLE_FEATURE", feature.Conditions[0].Condition);
        Assert.Equal(0, feature.Conditions[0].Level);
    }

    [Fact]
    public void FeatureBuilder_NoConditions_HasEmptyConditionsList()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Main", f => f.Title = "Main Feature");
        });

        var feature = package.Features.First(f => f.Id == "Main");
        Assert.Empty(feature.Conditions);
    }

    [Fact]
    public void FeatureBuilder_ChildFeature_HasOwnConditions()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Parent", f =>
            {
                f.Title = "Parent";
                f.Condition("PARENT_COND", 1);
                f.Feature("Child", child =>
                {
                    child.Title = "Child";
                    child.Condition("CHILD_COND", 0);
                });
            });
        });

        var parent = package.Features.First(f => f.Id == "Parent");
        Assert.Single(parent.Conditions);
        Assert.Equal("PARENT_COND", parent.Conditions[0].Condition);

        var child = parent.Children.First(f => f.Id == "Child");
        Assert.Single(child.Conditions);
        Assert.Equal("CHILD_COND", child.Conditions[0].Condition);
    }

    [Fact]
    public void Validation_EmptyConditionString_ProducesFEA004Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main",
                    Conditions =
                    [
                        new FeatureConditionModel { Condition = "", Level = 0 }
                    ]
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "FEA004");
    }

    [Fact]
    public void Validation_WhitespaceConditionString_ProducesFEA004Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main",
                    Conditions =
                    [
                        new FeatureConditionModel { Condition = "   ", Level = 0 }
                    ]
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "FEA004");
    }

    [Fact]
    public void Validation_NegativeLevel_ProducesFEA005Warning()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main",
                    Conditions =
                    [
                        new FeatureConditionModel { Condition = "SOME_COND", Level = -1 }
                    ]
                }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "FEA005");
    }

    [Fact]
    public void Validation_ValidCondition_NoErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Optional", f =>
            {
                f.Title = "Optional";
                f.Condition("NOT REMOVE", 0);
            });
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }
}
