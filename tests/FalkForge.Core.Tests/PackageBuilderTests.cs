using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class PackageBuilderTests
{
    [Fact]
    public void Build_WithNameAndManufacturer_SetsProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        });

        Assert.Equal("TestApp", package.Name);
        Assert.Equal("TestCorp", package.Manufacturer);
    }

    [Fact]
    public void Build_HasCorrectDefaults()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Equal(InstallScope.PerMachine, package.Scope);
        Assert.Equal(ProcessorArchitecture.X64, package.Architecture);
        Assert.Equal(CompressionLevel.High, package.Compression);
        Assert.Equal(new Version(1, 0, 0), package.Version);
    }

    [Fact]
    public void Files_AddsFileEntries()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "App"));
        });

        Assert.Single(package.Files);
        Assert.Equal("app.exe", package.Files[0].FileName);
    }

    [Fact]
    public void Files_MultipleFiles_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Files(f => f
                .Add("app.exe")
                .Add("config.json")
                .To(KnownFolder.ProgramFiles / "App"));
        });

        Assert.Equal(2, package.Files.Count);
        Assert.Equal("app.exe", package.Files[0].FileName);
        Assert.Equal("config.json", package.Files[1].FileName);
    }

    [Fact]
    public void Feature_AddsFeaturesToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Core", f =>
            {
                f.Title = "Core Feature";
                f.IsRequired = true;
            });
        });

        Assert.Single(package.Features);
        Assert.Equal("Core", package.Features[0].Id);
        Assert.Equal("Core Feature", package.Features[0].Title);
        Assert.True(package.Features[0].IsRequired);
    }

    [Fact]
    public void Shortcut_OnDesktop_AddsShortcut()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe").OnDesktop();
        });

        Assert.Single(package.Shortcuts);
        Assert.Equal("My App", package.Shortcuts[0].Name);
        Assert.Equal("app.exe", package.Shortcuts[0].TargetFile);
        Assert.Contains(ShortcutLocation.Desktop, package.Shortcuts[0].Locations);
    }

    [Fact]
    public void Shortcut_OnStartMenu_AddsShortcut()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe").OnStartMenu("MyCompany");
        });

        Assert.Single(package.Shortcuts);
        Assert.Contains(ShortcutLocation.StartMenu, package.Shortcuts[0].Locations);
    }

    [Fact]
    public void Service_AddsServiceToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DisplayName = "My Service";
                svc.StartMode = ServiceStartMode.Automatic;
            });
        });

        Assert.Single(package.Services);
        Assert.Equal("MySvc", package.Services[0].Name);
        Assert.Equal("svc.exe", package.Services[0].Executable);
        Assert.Equal("My Service", package.Services[0].DisplayName);
    }

    [Fact]
    public void Registry_AddsRegistryEntries()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Registry(r => r.Key(RegistryRoot.LocalMachine, @"SOFTWARE\MyApp", k =>
                k.Value("Version", "1.0")));
        });

        Assert.Single(package.RegistryEntries);
        Assert.Equal(RegistryRoot.LocalMachine, package.RegistryEntries[0].Root);
        Assert.Equal(@"SOFTWARE\MyApp", package.RegistryEntries[0].Key);
    }

    [Fact]
    public void EnvironmentVariable_AddsEnvVar()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.EnvironmentVariable("MY_VAR", "my_value");
        });

        Assert.Single(package.EnvironmentVariables);
        Assert.Equal("MY_VAR", package.EnvironmentVariables[0].Name);
        Assert.Equal("my_value", package.EnvironmentVariables[0].Value);
    }

    [Fact]
    public void Property_AddsPropertyToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Property("INSTALL_MODE", "full");
        });

        Assert.Single(package.Properties);
        Assert.Equal("INSTALL_MODE", package.Properties[0].Name);
        Assert.Equal("full", package.Properties[0].Value);
    }

    [Fact]
    public void Require_AddsLaunchCondition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Require("VersionNT >= 603", "Windows 8.1 or later is required.");
        });

        Assert.Single(package.LaunchConditions);
        Assert.Equal("VersionNT >= 603", package.LaunchConditions[0].Condition);
        Assert.Equal("Windows 8.1 or later is required.", package.LaunchConditions[0].Message);
    }

    [Fact]
    public void Upgrade_ConfiguresUpgradeSettings()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Upgrade(u =>
            {
                u.AllowDowngrades = true;
                u.AllowSameVersion = true;
            });
        });

        Assert.NotNull(package.Upgrade);
        Assert.True(package.Upgrade.AllowDowngrades);
        Assert.True(package.Upgrade.AllowSameVersion);
    }

    [Fact]
    public void Build_GeneratesDeterministicUpgradeCode_FromNameAndManufacturer()
    {
        var package1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });
        var package2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Equal(package1.UpgradeCode, package2.UpgradeCode);
        Assert.NotEqual(Guid.Empty, package1.UpgradeCode);
    }

    [Fact]
    public void Build_DifferentNameOrManufacturer_ProducesDifferentUpgradeCode()
    {
        var package1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App1";
            p.Manufacturer = "Corp";
        });
        var package2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App2";
            p.Manufacturer = "Corp";
        });

        Assert.NotEqual(package1.UpgradeCode, package2.UpgradeCode);
    }

    [Fact]
    public void Build_ExplicitUpgradeCode_UsesProvidedValue()
    {
        var explicitGuid = Guid.NewGuid();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.UpgradeCode = explicitGuid;
        });

        Assert.Equal(explicitGuid, package.UpgradeCode);
    }

    [Fact]
    public void Build_NoFeaturesExplicit_CreatesImplicitCompleteFeature()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.Single(package.Features);
        Assert.Equal("Complete", package.Features[0].Id);
        Assert.Equal("Complete", package.Features[0].Title);
        Assert.True(package.Features[0].IsRequired);
        Assert.True(package.Features[0].IsDefault);
    }

    [Fact]
    public void Build_WithExplicitFeatures_DoesNotCreateImplicitFeature()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Custom", f => f.Title = "Custom");
        });

        Assert.Single(package.Features);
        Assert.Equal("Custom", package.Features[0].Id);
    }

    [Fact]
    public void Build_DefaultInstallDirectory_DerivedFromManufacturerAndName()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MyApp";
            p.Manufacturer = "Contoso";
        });

        Assert.NotNull(package.DefaultInstallDirectory);
        var expectedPath = KnownFolder.ProgramFiles / "Contoso" / "MyApp";
        Assert.Equal(expectedPath, package.DefaultInstallDirectory);
    }

    [Fact]
    public void Build_ExplicitInstallDirectory_UsesProvidedValue()
    {
        var customDir = KnownFolder.CommonAppData / "MyApp";
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MyApp";
            p.Manufacturer = "Contoso";
            p.DefaultInstallDirectory = customDir;
        });

        Assert.Equal(customDir, package.DefaultInstallDirectory);
    }

    [Fact]
    public void Build_SetsDescription()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Description = "A test application";
        });

        Assert.Equal("A test application", package.Description);
    }

    [Fact]
    public void Build_ProductCode_IsGenerated()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        Assert.NotEqual(Guid.Empty, package.ProductCode);
    }

    [Fact]
    public void Build_ExplicitProductCode_UsesProvidedValue()
    {
        var code = Guid.NewGuid();
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ProductCode = code;
        });

        Assert.Equal(code, package.ProductCode);
    }

    [Fact]
    public void Reproducible_ProductCode_IsDeterministicAcrossBuilds()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });

        Assert.Equal(p1.ProductCode, p2.ProductCode);
        Assert.NotEqual(Guid.Empty, p1.ProductCode);
    }

    [Fact]
    public void Reproducible_ProductCode_DiffersForDifferentVersion()
    {
        var p1 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(1, 0, 0);
            p.Reproducible(1708600000L);
        });
        var p2 = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App"; p.Manufacturer = "Corp"; p.Version = new Version(2, 0, 0);
            p.Reproducible(1708600000L);
        });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }

    [Fact]
    public void NonReproducible_ProductCode_VariesAcrossBuilds()
    {
        var p1 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });
        var p2 = InstallerTestHost.BuildPackage(p => { p.Name = "App"; p.Manufacturer = "Corp"; });

        Assert.NotEqual(p1.ProductCode, p2.ProductCode);
    }
}
