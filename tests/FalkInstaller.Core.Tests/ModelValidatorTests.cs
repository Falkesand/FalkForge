using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class ModelValidatorTests
{
    private static PackageModel BuildValidPackage()
    {
        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "TestCorp" / "TestApp"));
        });
    }

    [Fact]
    public void ValidPackage_PassesValidation()
    {
        var package = BuildValidPackage();

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MissingName_ProducesPKG001Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "";
            p.Manufacturer = "Corp";
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG001");
    }

    [Fact]
    public void MissingManufacturer_ProducesPKG002Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "";
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG002");
    }

    [Fact]
    public void MajorVersionAbove255_ProducesPKG005Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(256, 0, 0);
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG005");
    }

    [Fact]
    public void MinorVersionAbove255_ProducesPKG006Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 256, 0);
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG006");
    }

    [Fact]
    public void BuildVersionAbove65535_ProducesPKG007Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 65536);
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG007");
    }

    [Fact]
    public void EmptyGuidUpgradeCode_ProducesPKG009Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.UpgradeCode = Guid.Empty;
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG009");
    }

    [Fact]
    public void EmptyGuidProductCode_ProducesPKG010Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ProductCode = Guid.Empty;
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "PKG010");
    }

    [Fact]
    public void PackageWithNoFiles_ProducesPKG011Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
        });

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "PKG011");
    }

    [Fact]
    public void DuplicateFeatureIds_ProduceFEA002Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Feature("Core", f => f.Title = "Core");
            p.Feature("Core", f => f.Title = "Core Duplicate");
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "FEA002");
    }

    [Fact]
    public void ServiceWithoutExecutable_ProducesSVC002Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.DisplayName = "My Service";
                // No Executable set
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SVC002");
    }

    [Fact]
    public void ServiceWithUserAccountButNoUsername_ProducesSVC003Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Account = ServiceAccount.User;
                // No UserName set
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SVC003");
    }

    [Fact]
    public void ShortcutWithoutTarget_ProducesSHC002Error()
    {
        // Build a package with an invalid shortcut directly via the model
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Shortcuts = [new ShortcutModel
            {
                Name = "MyShortcut",
                TargetFile = "",
                Locations = [ShortcutLocation.Desktop]
            }],
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
        Assert.Contains(result.Errors, e => e.Code == "SHC002");
    }

    [Fact]
    public void ValidPackage_WithAllProperties_PassesValidation()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "FullApp";
            p.Manufacturer = "FullCorp";
            p.Version = new Version(2, 1, 500);
            p.Description = "Full test app";
            p.HelpUrl = "https://example.com";
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "FullCorp" / "FullApp"));
            p.Feature("Main", f =>
            {
                f.Title = "Main";
                f.IsRequired = true;
            });
            p.Service("Svc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DisplayName = "Service";
            });
            p.Shortcut("App", "app.exe").OnDesktop();
            p.Require("VersionNT >= 603", "Requires Win 8.1+");
            p.Property("MODE", "full");
            p.EnvironmentVariable("APP_HOME", "C:\\App");
        });

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Version000_ProducesPKG004Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(0, 0, 0);
        });

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "PKG004");
    }

    [Fact]
    public void Validate_ServiceWithPlaintextPassword_EmitsSVC005Warning()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Account = ServiceAccount.User;
                svc.UserName = @"DOMAIN\svcuser";
                svc.Password = "P@ssw0rd!";
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.Code == "SVC005");
        Assert.Contains(result.Warnings, w => w.Message.Contains("plaintext password"));
    }

    [Fact]
    public void Validate_ServiceWithoutPassword_DoesNotEmitSVC005()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Warnings, w => w.Code == "SVC005");
    }

    [Fact]
    public void ValidateAndAssertValid_OnInvalidPackage_Throws()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "";
            p.Manufacturer = "";
        });

        Assert.Throws<InvalidOperationException>(() =>
            InstallerValidator.ValidateAndAssertValid(package));
    }
}
