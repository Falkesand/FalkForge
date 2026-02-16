using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class FontTests
{
    [Fact]
    public void FontBuilder_SetsFileName()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Font("arial.ttf");
        });

        Assert.Single(package.Fonts);
        Assert.Equal("arial.ttf", package.Fonts[0].FileName);
    }

    [Fact]
    public void FontBuilder_SetsTitle()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Font("arial.ttf", f => f.Title = "Arial");
        });

        Assert.Single(package.Fonts);
        Assert.Equal("Arial", package.Fonts[0].FontTitle);
    }

    [Fact]
    public void PackageBuilder_AddFont_AddsToModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Font("font1.ttf");
            p.Font("font2.otf", f => f.Title = "Custom Font");
        });

        Assert.Equal(2, package.Fonts.Count);
        Assert.Equal("font1.ttf", package.Fonts[0].FileName);
        Assert.Null(package.Fonts[0].FontTitle);
        Assert.Equal("font2.otf", package.Fonts[1].FileName);
        Assert.Equal("Custom Font", package.Fonts[1].FontTitle);
    }

    [Fact]
    public void Validate_FontWithEmptyFileName_ProducesFNT001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Fonts = [new FontModel { FileName = "" }],
            Features = [new FeatureModel
            {
                Id = "Complete",
                Title = "Complete",
                IsRequired = true,
                IsDefault = true
            }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "FNT001");
    }
}
