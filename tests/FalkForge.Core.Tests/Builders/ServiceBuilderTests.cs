using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class ServiceBuilderTests
{
    [Fact]
    public void Build_SetsArguments()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Arguments = "--config C:\\app\\config.json";
            });
        });

        Assert.Equal("--config C:\\app\\config.json", package.Services[0].Arguments);
    }

    [Fact]
    public void Build_Arguments_DefaultsToNull()
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

        Assert.Null(package.Services[0].Arguments);
    }

    [Fact]
    public void Build_SetsAccountProperty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.AccountProperty("SERVICE_ACCOUNT");
            });
        });

        Assert.Equal("SERVICE_ACCOUNT", package.Services[0].AccountProperty);
    }

    [Fact]
    public void Build_AccountProperty_DefaultsToNull()
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

        Assert.Null(package.Services[0].AccountProperty);
    }

    [Fact]
    public void Build_SetsComponentCondition_String()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Condition("INSTALL_SERVICE");
            });
        });

        Assert.Equal("INSTALL_SERVICE", package.Services[0].ComponentCondition);
    }

    [Fact]
    public void Build_SetsComponentCondition_TypedCondition()
    {
        var condition = Condition.Raw("INSTALL_SERVICE = \"1\"");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.Condition(condition);
            });
        });

        Assert.Equal(condition.ToString(), package.Services[0].ComponentCondition);
    }

    [Fact]
    public void Build_ComponentCondition_DefaultsToNull()
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

        Assert.Null(package.Services[0].ComponentCondition);
    }

    [Fact]
    public void Validate_EmptyArguments_ProducesSVC009Warning()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Services =
            [
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Service",
                    Executable = "svc.exe",
                    Arguments = ""
                }
            ],
            Features =
            [
                new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "SVC009");
    }

    [Fact]
    public void Validate_AccountPropertyWithUserNameSet_ProducesSVC010Warning()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Services =
            [
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Service",
                    Executable = "svc.exe",
                    Account = ServiceAccount.User,
                    UserName = @"DOMAIN\svcuser",
                    AccountProperty = "SERVICE_ACCOUNT"
                }
            ],
            Features =
            [
                new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "SVC010");
    }

    [Fact]
    public void Validate_EmptyComponentCondition_ProducesSVC011Error()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            Services =
            [
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Service",
                    Executable = "svc.exe",
                    ComponentCondition = ""
                }
            ],
            Features =
            [
                new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }
            ]
        };

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "SVC011");
    }
}
