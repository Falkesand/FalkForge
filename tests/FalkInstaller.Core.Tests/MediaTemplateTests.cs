using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class MediaTemplateTests
{
    [Fact]
    public void MediaTemplateBuilder_Defaults_SetsCorrectValues()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => { });
        });

        Assert.NotNull(package.MediaTemplate);
        Assert.Equal("cab{0}.cab", package.MediaTemplate.CabinetTemplate);
        Assert.Equal(0, package.MediaTemplate.MaximumCabinetSizeInMB);
        Assert.Equal(0, package.MediaTemplate.MaximumUncompressedMediaSize);
        Assert.Equal(CompressionLevel.High, package.MediaTemplate.CompressionLevel);
        Assert.True(package.MediaTemplate.EmbedCabinet);
    }

    [Fact]
    public void MediaTemplateBuilder_CabinetTemplate_SetsPattern()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt.CabinetTemplate("media{0}.cab"));
        });

        Assert.Equal("media{0}.cab", package.MediaTemplate!.CabinetTemplate);
    }

    [Fact]
    public void MediaTemplateBuilder_MaxCabinetSizeMB_SetsSize()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt.MaxCabinetSizeMB(100));
        });

        Assert.Equal(100, package.MediaTemplate!.MaximumCabinetSizeInMB);
    }

    [Fact]
    public void MediaTemplateBuilder_CompressionLevel_SetsLevel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt.CompressionLevel(CompressionLevel.Low));
        });

        Assert.Equal(CompressionLevel.Low, package.MediaTemplate!.CompressionLevel);
    }

    [Fact]
    public void MediaTemplateBuilder_EmbedCabinet_SetsFlag()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt.EmbedCabinet(false));
        });

        Assert.False(package.MediaTemplate!.EmbedCabinet);
    }

    [Fact]
    public void MediaTemplateBuilder_AllFluentMethods_ChainCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt
                .CabinetTemplate("data{0}.cab")
                .MaxCabinetSizeMB(200)
                .MaxUncompressedMediaSize(500)
                .CompressionLevel(CompressionLevel.Medium)
                .EmbedCabinet(false));
        });

        var mt = package.MediaTemplate!;
        Assert.Equal("data{0}.cab", mt.CabinetTemplate);
        Assert.Equal(200, mt.MaximumCabinetSizeInMB);
        Assert.Equal(500, mt.MaximumUncompressedMediaSize);
        Assert.Equal(CompressionLevel.Medium, mt.CompressionLevel);
        Assert.False(mt.EmbedCabinet);
    }

    [Fact]
    public void Validate_MediaTemplateMissingPlaceholder_ProducesMDT002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MediaTemplate = new MediaTemplateModel
            {
                CabinetTemplate = "data.cab"
            },
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MDT002");
    }

    [Fact]
    public void Validate_ValidMediaTemplate_NoErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MediaTemplate(mt => mt.CabinetTemplate("cab{0}.cab").MaxCabinetSizeMB(50));
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("MDT"));
    }

    [Fact]
    public void PackageBuilder_NoMediaTemplate_IsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Null(package.MediaTemplate);
    }
}
