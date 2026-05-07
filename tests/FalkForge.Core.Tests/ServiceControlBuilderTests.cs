using System.Reflection;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class ServiceControlBuilderTests
{
    private static ServiceControlModel BuildModel(ServiceControlBuilder builder)
    {
        var buildMethod = typeof(ServiceControlBuilder).GetMethod("Build",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (ServiceControlModel)buildMethod!.Invoke(builder, null)!;
    }

    [Fact]
    public void Build_SetsIdAndServiceName()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC_MyService").ServiceName("MyService");

        var model = BuildModel(builder);

        Assert.Equal("SC_MyService", model.Id);
        Assert.Equal("MyService", model.ServiceName);
    }

    [Fact]
    public void StartOnInstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").StartOnInstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.StartOnInstall));
    }

    [Fact]
    public void StopOnInstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").StopOnInstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.StopOnInstall));
    }

    [Fact]
    public void DeleteOnInstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").DeleteOnInstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.DeleteOnInstall));
    }

    [Fact]
    public void StartOnUninstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").StartOnUninstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.StartOnUninstall));
    }

    [Fact]
    public void StopOnUninstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").StopOnUninstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.StopOnUninstall));
    }

    [Fact]
    public void DeleteOnUninstall_SetsFlag()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").DeleteOnUninstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.DeleteOnUninstall));
    }

    [Fact]
    public void MultipleEvents_CombinesFlags()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc")
            .StartOnInstall()
            .StopOnUninstall()
            .DeleteOnUninstall();

        var model = BuildModel(builder);

        Assert.True(model.Events.HasFlag(ServiceControlEvent.StartOnInstall));
        Assert.True(model.Events.HasFlag(ServiceControlEvent.StopOnUninstall));
        Assert.True(model.Events.HasFlag(ServiceControlEvent.DeleteOnUninstall));
        Assert.Equal(ServiceControlEvent.StartOnInstall | ServiceControlEvent.StopOnUninstall | ServiceControlEvent.DeleteOnUninstall, model.Events);
    }

    [Fact]
    public void DefaultWait_IsTrue()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc");

        var model = BuildModel(builder);

        Assert.True(model.Wait);
    }

    [Fact]
    public void Wait_False_SetsWaitToFalse()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").Wait(false);

        var model = BuildModel(builder);

        Assert.False(model.Wait);
    }

    [Fact]
    public void Arguments_SetsArguments()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC1").ServiceName("Svc").Arguments("-config test.conf");

        var model = BuildModel(builder);

        Assert.Equal("-config test.conf", model.Arguments);
    }

    [Fact]
    public void ComponentRef_SetsProperty()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC_CR").ServiceName("Svc").StartOnInstall().ComponentRef("SvcComponent");

        var model = BuildModel(builder);

        Assert.Equal("SvcComponent", model.ComponentRef);
    }

    [Fact]
    public void DefaultComponentRef_IsNull()
    {
        var builder = new ServiceControlBuilder();
        builder.Id("SC_DEF").ServiceName("Svc").StartOnInstall();

        var model = BuildModel(builder);

        Assert.Null(model.ComponentRef);
    }

    [Fact]
    public void FluentChaining_AllMethods_ReturnBuilder()
    {
        var builder = new ServiceControlBuilder();
        var result = builder
            .Id("SC1")
            .ServiceName("Svc")
            .StartOnInstall()
            .StopOnInstall()
            .DeleteOnInstall()
            .StartOnUninstall()
            .StopOnUninstall()
            .DeleteOnUninstall()
            .Wait(true)
            .Arguments("--flag")
            .ComponentRef("Comp");

        Assert.Same(builder, result);
    }
}

public sealed class ServiceDependencyTests
{
    [Fact]
    public void DependsOn_AddsSingleDependency()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DependsOn("OtherService");
            });
        });

        var service = package.Services[0];
        Assert.Single(service.TypedDependencies);
        Assert.Equal("OtherService", service.TypedDependencies[0].DependsOn);
        Assert.False(service.TypedDependencies[0].Group);
        Assert.Equal("MySvc", service.TypedDependencies[0].ServiceName);
    }

    [Fact]
    public void DependsOn_MultipleDependencies_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DependsOn("ServiceA");
                svc.DependsOn("ServiceB");
            });
        });

        var service = package.Services[0];
        Assert.Equal(2, service.TypedDependencies.Count);
        Assert.Equal("ServiceA", service.TypedDependencies[0].DependsOn);
        Assert.Equal("ServiceB", service.TypedDependencies[1].DependsOn);
    }

    [Fact]
    public void DependsOnGroup_SetsGroupFlag()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DependsOnGroup("NetworkProviders");
            });
        });

        var service = package.Services[0];
        Assert.Single(service.TypedDependencies);
        Assert.Equal("NetworkProviders", service.TypedDependencies[0].DependsOn);
        Assert.True(service.TypedDependencies[0].Group);
    }

    [Fact]
    public void DependsOn_MixedWithGroup_PreservesOrder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DependsOn("ServiceA");
                svc.DependsOnGroup("NetworkGroup");
                svc.DependsOn("ServiceB");
            });
        });

        var service = package.Services[0];
        Assert.Equal(3, service.TypedDependencies.Count);
        Assert.Equal("ServiceA", service.TypedDependencies[0].DependsOn);
        Assert.False(service.TypedDependencies[0].Group);
        Assert.Equal("NetworkGroup", service.TypedDependencies[1].DependsOn);
        Assert.True(service.TypedDependencies[1].Group);
        Assert.Equal("ServiceB", service.TypedDependencies[2].DependsOn);
        Assert.False(service.TypedDependencies[2].Group);
    }
}

public sealed class ServiceControlValidationTests
{
    [Fact]
    public void ServiceControl_MissingServiceName_ProducesSCT001Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ServiceControl(sc => sc
                .Id("SC1")
                .ServiceName("")
                .StartOnInstall());
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "SCT001");
    }

    [Fact]
    public void ServiceControl_NoEvents_ProducesSCT002Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ServiceControl(sc => sc
                .Id("SC1")
                .ServiceName("MySvc"));
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "SCT002");
    }

    [Fact]
    public void ServiceControl_ValidConfiguration_Passes()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ServiceControl(sc => sc
                .Id("SC_MySvc")
                .ServiceName("MySvc")
                .StartOnInstall()
                .StopOnUninstall());
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "SCT001");
        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "SCT002");
    }

    [Fact]
    public void ServiceDependency_EmptyDependsOn_ProducesSDP001Error()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MySvc", svc =>
            {
                svc.Executable = "svc.exe";
                svc.DependsOn("");
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "SDP001");
    }
}

public sealed class ServiceControlPackageBuilderTests
{
    [Fact]
    public void PackageBuilder_ServiceControl_AddsToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ServiceControl(sc => sc
                .Id("SC_Legacy")
                .ServiceName("LegacySvc")
                .StopOnInstall()
                .DeleteOnInstall());
        });

        Assert.Single(package.ServiceControls);
        Assert.Equal("SC_Legacy", package.ServiceControls[0].Id);
        Assert.Equal("LegacySvc", package.ServiceControls[0].ServiceName);
        Assert.True(package.ServiceControls[0].Events.HasFlag(ServiceControlEvent.StopOnInstall));
        Assert.True(package.ServiceControls[0].Events.HasFlag(ServiceControlEvent.DeleteOnInstall));
    }

    [Fact]
    public void PackageBuilder_MultipleServiceControls_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ServiceControl(sc => sc
                .Id("SC1")
                .ServiceName("Svc1")
                .StartOnInstall());
            p.ServiceControl(sc => sc
                .Id("SC2")
                .ServiceName("Svc2")
                .StopOnUninstall());
        });

        Assert.Equal(2, package.ServiceControls.Count);
        Assert.Equal("SC1", package.ServiceControls[0].Id);
        Assert.Equal("SC2", package.ServiceControls[1].Id);
    }
}

public sealed class ServiceControlEventTests
{
    [Fact]
    public void EventBitValues_MatchMsiSpec()
    {
        Assert.Equal(1, (int)ServiceControlEvent.StartOnInstall);
        Assert.Equal(2, (int)ServiceControlEvent.StopOnInstall);
        Assert.Equal(8, (int)ServiceControlEvent.DeleteOnInstall);
        Assert.Equal(16, (int)ServiceControlEvent.StartOnUninstall);
        Assert.Equal(32, (int)ServiceControlEvent.StopOnUninstall);
        Assert.Equal(128, (int)ServiceControlEvent.DeleteOnUninstall);
    }

    [Fact]
    public void EventFlags_CanBeCombined()
    {
        var combined = ServiceControlEvent.StartOnInstall | ServiceControlEvent.StopOnUninstall;
        var expectedValue = 1 | 32;

        Assert.Equal(expectedValue, (int)combined);
    }
}
