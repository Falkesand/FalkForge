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

        Assert.DoesNotContain(result.Errors, e => e.Code == "PRM003");
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

        Assert.Contains(result.Errors, e => e.Code == "PRM003");
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
