using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class IniFileTests
{
    [Fact]
    public void IniFileBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.IniFile("config.ini", ini => ini
                .Section("Settings")
                .Key("Theme")
                .Value("Dark")
                .Action(IniFileAction.CreateEntry));
        });

        Assert.Single(package.IniFiles);
        var ini = package.IniFiles[0];
        Assert.Equal("config.ini", ini.FileName);
        Assert.Equal("Settings", ini.Section);
        Assert.Equal("Theme", ini.Key);
        Assert.Equal("Dark", ini.Value);
        Assert.Equal(IniFileAction.CreateEntry, ini.Action);
    }

    [Fact]
    public void IniFileBuilder_DefaultsActionToCreateEntry()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.IniFile("config.ini", ini => ini
                .Section("Settings")
                .Key("Theme")
                .Value("Dark"));
        });

        Assert.Equal(IniFileAction.CreateEntry, package.IniFiles[0].Action);
    }

    [Fact]
    public void PackageBuilder_MultipleIniFiles_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.IniFile("config.ini", ini => ini.Section("S1").Key("K1").Value("V1"));
            p.IniFile("settings.ini", ini => ini.Section("S2").Key("K2").Value("V2"));
        });

        Assert.Equal(2, package.IniFiles.Count);
        Assert.Equal("config.ini", package.IniFiles[0].FileName);
        Assert.Equal("settings.ini", package.IniFiles[1].FileName);
    }

    [Fact]
    public void IniFileBuilder_RemoveLineAction()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.IniFile("config.ini", ini => ini
                .Section("Settings")
                .Key("OldKey")
                .Value("")
                .Action(IniFileAction.RemoveLine));
        });

        Assert.Equal(IniFileAction.RemoveLine, package.IniFiles[0].Action);
    }

    [Fact]
    public void Validate_IniFileWithEmptyFileName_ProducesINI001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            IniFiles = [new IniFileModel { FileName = "", Section = "S", Key = "K", Value = "V" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INI001");
    }

    [Fact]
    public void Validate_IniFileWithEmptySection_ProducesINI002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            IniFiles = [new IniFileModel { FileName = "config.ini", Section = "", Key = "K", Value = "V" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INI002");
    }

    [Fact]
    public void Validate_IniFileWithEmptyKey_ProducesINI003()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            IniFiles = [new IniFileModel { FileName = "config.ini", Section = "S", Key = "", Value = "V" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INI003");
    }

    [Fact]
    public void Validate_ValidIniFile_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            IniFiles = [new IniFileModel { FileName = "config.ini", Section = "Settings", Key = "Theme", Value = "Dark" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void IniFileAction_EnumValues_MatchMsiSpec()
    {
        Assert.Equal(0, (int)IniFileAction.CreateLine);
        Assert.Equal(1, (int)IniFileAction.CreateEntry);
        Assert.Equal(2, (int)IniFileAction.RemoveLine);
        Assert.Equal(3, (int)IniFileAction.RemoveTag);
    }
}
