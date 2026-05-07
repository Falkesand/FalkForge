using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class PermissionTests
{
    [Fact]
    public void PermissionBuilder_SddlBased_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Permission("INSTALLDIR", perm =>
            {
                perm.Sddl = "D:(A;;GA;;;BA)";
                perm.ForTable("CreateFolder");
            });
        });

        Assert.Single(package.Permissions);
        var perm = package.Permissions[0];
        Assert.Equal("INSTALLDIR", perm.LockObject);
        Assert.Equal("CreateFolder", perm.Table);
        Assert.Equal("D:(A;;GA;;;BA)", perm.Sddl);
    }

    [Fact]
    public void PermissionBuilder_UserBased_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Permission("DataFile.txt", perm =>
            {
                perm.ForTable("File");
                perm.Domain = "BUILTIN";
                perm.User = "Users";
                perm.Permission = 0x001F01FF; // Full control
            });
        });

        Assert.Single(package.Permissions);
        var perm = package.Permissions[0];
        Assert.Equal("DataFile.txt", perm.LockObject);
        Assert.Equal("File", perm.Table);
        Assert.Equal("BUILTIN", perm.Domain);
        Assert.Equal("Users", perm.User);
        Assert.Equal(0x001F01FF, perm.Permission);
    }

    [Fact]
    public void PermissionBuilder_DefaultsTableToCreateFolder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Permission("INSTALLDIR", perm =>
            {
                perm.Sddl = "D:(A;;GA;;;BA)";
            });
        });

        Assert.Equal("CreateFolder", package.Permissions[0].Table);
    }

    [Fact]
    public void PackageBuilder_MultiplePermissions_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Permission("INSTALLDIR", perm =>
            {
                perm.Sddl = "D:(A;;GA;;;BA)";
            });
            p.Permission("config.dat", perm =>
            {
                perm.ForTable("File");
                perm.User = "Everyone";
                perm.Permission = 0x00000001; // Read
            });
        });

        Assert.Equal(2, package.Permissions.Count);
        Assert.Equal("INSTALLDIR", package.Permissions[0].LockObject);
        Assert.Equal("config.dat", package.Permissions[1].LockObject);
    }

    [Fact]
    public void Validate_PermissionWithEmptyLockObject_ProducesPRM001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Permissions = [new PermissionModel { LockObject = "", Table = "File", Sddl = "D:(A;;GA;;;BA)" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "PRM001");
    }

    [Fact]
    public void Validate_PermissionWithoutSddlOrUser_ProducesPRM002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Permissions = [new PermissionModel { LockObject = "INSTALLDIR", Table = "CreateFolder" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "PRM002");
    }

    [Fact]
    public void Validate_PermissionWithSddl_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Permissions = [new PermissionModel { LockObject = "INSTALLDIR", Table = "CreateFolder", Sddl = "D:(A;;GA;;;BA)" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_PermissionWithUser_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Permissions = [new PermissionModel { LockObject = "INSTALLDIR", Table = "CreateFolder", User = "Everyone", Permission = 1 }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void PermissionBuilder_ForTable_ChangesTable()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Permission("RegKey", perm =>
            {
                perm.ForTable("Registry");
                perm.User = "Administrators";
                perm.Permission = 0x000F003F;
            });
        });

        Assert.Equal("Registry", package.Permissions[0].Table);
    }
}
