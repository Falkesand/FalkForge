using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class ServiceBuilderPermissionTests
{
    [Fact]
    public void Permission_AddsSddlPermission_ToServiceModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MyService", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Permission(perm => perm.Sddl = "D:(A;;RPWP;;;WD)");
            });
        });

        var service = package.Services[0];
        Assert.Single(service.Permissions);
        var perm = service.Permissions[0];
        Assert.Equal("MyService", perm.LockObject);
        Assert.Equal("ServiceInstall", perm.Table);
        Assert.Equal("D:(A;;RPWP;;;WD)", perm.Sddl);
    }

    [Fact]
    public void Permission_MultiplePermissions_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Permission(perm => perm.Sddl = "D:(A;;RPWP;;;WD)");
                svc.Permission(perm =>
                {
                    perm.Domain = "BUILTIN";
                    perm.User = "Administrators";
                    perm.Permission = 0x000F01FF;
                });
            });
        });

        var service = package.Services[0];
        Assert.Equal(2, service.Permissions.Count);
        Assert.All(service.Permissions, p =>
        {
            Assert.Equal("MySvc", p.LockObject);
            Assert.Equal("ServiceInstall", p.Table);
        });
    }

    [Fact]
    public void Permission_DefaultPermissions_IsEmpty()
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

        Assert.Empty(package.Services[0].Permissions);
    }
}
