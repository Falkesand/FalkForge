using FalkForge.Builders;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Services and registry entries added via FeatureBuilder.Service()/.Registry() must survive
/// the PackageBuilder.Feature() call and appear in PackageModel.Services/RegistryEntries with
/// the correct FeatureRef, so the compiler can assign their synthesized component to the right
/// MSI feature. Mirrors FeatureBuilderFilesTests, which proves the same contract for files.
/// </summary>
public sealed class FeatureBuilderServiceRegistryTests
{
    [Fact]
    public void Feature_AddService_ScopedServiceReachesPackageModel()
    {
        // WHY: a service declared via FeatureBuilder.Service() must reach PackageModel.Services
        // so the MSI compiler can gate its ServiceInstall row to the right feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Service("AppSvc", s =>
                {
                    s.Executable = @"C:\payload\svc.exe";
                });
            });
        });

        Assert.Single(package.Services);
    }

    [Fact]
    public void Feature_AddService_ScopedServiceHasCorrectFeatureRef()
    {
        // WHY: without a correct FeatureRef the compiler cannot place the service's component
        // under the intended feature, silently defaulting it to "Complete" or the first feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Service("AppSvc", s =>
                {
                    s.Executable = @"C:\payload\svc.exe";
                });
            });
        });

        var service = Assert.Single(package.Services);
        Assert.Equal("ServerFeature", service.FeatureRef);
    }

    [Fact]
    public void Feature_AddRegistry_ScopedEntryReachesPackageModel()
    {
        // WHY: a registry entry declared via FeatureBuilder.Registry() must reach
        // PackageModel.RegistryEntries so the compiler can gate its Registry row.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\Acme\App", k =>
                    k.Value("InstallPath", @"C:\payload")));
            });
        });

        Assert.Single(package.RegistryEntries);
    }

    [Fact]
    public void Feature_AddRegistry_ScopedEntryHasCorrectFeatureRef()
    {
        // WHY: without a correct FeatureRef the compiler cannot place the registry entry's
        // component under the intended feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("ServerFeature", f =>
            {
                f.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\Acme\App", k =>
                    k.Value("InstallPath", @"C:\payload")));
            });
        });

        var entry = Assert.Single(package.RegistryEntries);
        Assert.Equal("ServerFeature", entry.FeatureRef);
    }

    [Fact]
    public void Feature_AddServiceAndRegistry_MultipleFeatures_EachGetsCorrectRef()
    {
        // WHY: when two features each declare a service/registry entry, each must carry its own
        // feature's ref, not bleed into the other feature or the default "Complete" feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("FeatureA", f =>
            {
                f.Service("SvcA", s => s.Executable = @"C:\payload\a.exe");
                f.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\Acme\A", k =>
                    k.Value("Marker", "a")));
            });
            p.Feature("FeatureB", f =>
            {
                f.Service("SvcB", s => s.Executable = @"C:\payload\b.exe");
                f.Registry(r => r.Key(RegistryRoot.LocalMachine, @"Software\Acme\B", k =>
                    k.Value("Marker", "b")));
            });
        });

        var svcA = Assert.Single(package.Services, s => s.Name == "SvcA");
        Assert.Equal("FeatureA", svcA.FeatureRef);
        var svcB = Assert.Single(package.Services, s => s.Name == "SvcB");
        Assert.Equal("FeatureB", svcB.FeatureRef);

        var entryA = Assert.Single(package.RegistryEntries, e => e.Key == @"Software\Acme\A");
        Assert.Equal("FeatureA", entryA.FeatureRef);
        var entryB = Assert.Single(package.RegistryEntries, e => e.Key == @"Software\Acme\B");
        Assert.Equal("FeatureB", entryB.FeatureRef);
    }
}
