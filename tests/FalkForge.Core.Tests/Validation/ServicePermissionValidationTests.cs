using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class ServicePermissionValidationTests
{
    [Fact]
    public void ValidatePermissions_ServiceInstallTable_IsAccepted()
    {
        var package = CreateMinimalPackage(permissions:
        [
            new PermissionModel
            {
                LockObject = "MyService",
                Table = "ServiceInstall",
                Sddl = "D:(A;;RPWP;;;WD)"
            }
        ]);

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "PRM003");
    }

    [Fact]
    public void ValidatePermissions_MixedSddlAndUserPermissions_ProducesPRM004()
    {
        // MSI rejects a package that materializes both LockPermissions (User/Domain)
        // and MsiLockPermissionsEx (SDDL) tables (ICE 1941). Catch the mix at compile time.
        var package = CreateMinimalPackage(permissions:
        [
            new PermissionModel
            {
                LockObject = "File1",
                Table = "File",
                Sddl = "D:(A;;RPWP;;;WD)"
            },
            new PermissionModel
            {
                LockObject = "File2",
                Table = "File",
                User = "Everyone",
                Permission = 0x10000000
            }
        ]);

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "PRM004");
    }

    [Fact]
    public void ValidatePermissions_InvalidTable_ProducesPRM003()
    {
        var package = CreateMinimalPackage(permissions:
        [
            new PermissionModel
            {
                LockObject = "Something",
                Table = "InvalidTable",
                Sddl = "D:(A;;RPWP;;;WD)"
            }
        ]);

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "PRM003");
    }

    private static PackageModel CreateMinimalPackage(IReadOnlyList<PermissionModel>? permissions = null)
    {
        return new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Permissions = permissions ?? [],
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main Feature"
                }
            ]
        };
    }
}
