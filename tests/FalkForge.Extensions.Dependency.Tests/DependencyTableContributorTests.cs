using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Extensions.Dependency.Tests;

public sealed class DependencyTableContributorTests
{
    private static ExtensionContext CreateContext(PackageModel? package = null)
    {
        return new ExtensionContext
        {
            Package = package ?? new PackageModel
            {
                Name = "TestApp",
                Manufacturer = "TestCo",
                Version = new Version(1, 0, 0),
                Files =
                [
                    new FileEntryModel
                    {
                        SourcePath = "app.exe",
                        TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                        FileName = "app.exe",
                        ComponentId = "comp_Default"
                    }
                ]
            },
            OutputDirectory = "out",
            SourceDirectory = "src"
        };
    }

    [Fact]
    public void GetRows_NoProviders_NoConsumers_ReturnsEmpty()
    {
        var sut = new DependencyTableContributor([], []);

        var rows = sut.GetRows(CreateContext());

        Assert.Empty(rows);
    }

    [Fact]
    public void GetRows_SingleProvider_EmitsKeyAndVersionRows()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0" }
        };
        var sut = new DependencyTableContributor(providers, []);

        var rows = sut.GetRows(CreateContext());

        Assert.Equal(2, rows.Count);
        Assert.Equal("dep_prov_MyApp", rows[0].Get("Registry"));
        Assert.Equal("MyApp", rows[0].Get("Value"));
        Assert.Equal("dep_prov_MyApp_ver", rows[1].Get("Registry"));
        Assert.Equal("Version", rows[1].Get("Name"));
        Assert.Equal("1.0.0", rows[1].Get("Value"));
    }

    [Fact]
    public void GetRows_ProviderWithDisplayName_EmitsThreeRows()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "2.0.0", DisplayName = "My Application" }
        };
        var sut = new DependencyTableContributor(providers, []);

        var rows = sut.GetRows(CreateContext());

        Assert.Equal(3, rows.Count);
        Assert.Equal("dep_prov_MyApp_name", rows[2].Get("Registry"));
        Assert.Equal("DisplayName", rows[2].Get("Name"));
        Assert.Equal("My Application", rows[2].Get("Value"));
    }

    [Fact]
    public void GetRows_SingleConsumer_EmitsSentinelRow()
    {
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "MyApp", ConsumerKey = "OtherApp" }
        };
        var sut = new DependencyTableContributor([], consumers);

        var rows = sut.GetRows(CreateContext());

        Assert.Single(rows);
        Assert.Equal("dep_cons_MyApp_OtherApp", rows[0].Get("Registry"));
        Assert.Null(rows[0].Get("Name"));
        Assert.Equal("", rows[0].Get("Value"));
    }

    [Fact]
    public void GetRows_ProviderRows_UseCorrectRegistryPaths()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "Contoso.MyApp", Version = "1.0.0" }
        };
        var sut = new DependencyTableContributor(providers, []);

        var rows = sut.GetRows(CreateContext());

        var expectedPath = @"SOFTWARE\Classes\Installer\Dependencies\Contoso.MyApp";
        Assert.Equal(expectedPath, rows[0].Get("Key"));
        Assert.Equal(expectedPath, rows[1].Get("Key"));
        Assert.Equal(2, rows[0].Get("Root"));
        Assert.Equal(2, rows[1].Get("Root"));
    }

    [Fact]
    public void GetRows_ConsumerRows_UseCorrectRegistryPaths()
    {
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "Contoso.SharedLib", ConsumerKey = "Contoso.MyApp" }
        };
        var sut = new DependencyTableContributor([], consumers);

        var rows = sut.GetRows(CreateContext());

        var expectedPath = @"SOFTWARE\Classes\Installer\Dependencies\Contoso.SharedLib\Dependents\Contoso.MyApp";
        Assert.Equal(expectedPath, rows[0].Get("Key"));
        Assert.Equal(2, rows[0].Get("Root"));
    }

    [Fact]
    public void GetRows_UsesComponentRefWhenProvided()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0", ComponentRef = "CustomComponent" }
        };
        var consumers = new List<DependencyConsumerModel>
        {
            new() { ProviderKey = "Lib", ConsumerKey = "MyApp", ComponentRef = "AnotherComponent" }
        };
        var sut = new DependencyTableContributor(providers, consumers);

        var rows = sut.GetRows(CreateContext());

        Assert.Equal("CustomComponent", rows[0].Get("Component_"));
        Assert.Equal("CustomComponent", rows[1].Get("Component_"));
        Assert.Equal("AnotherComponent", rows[2].Get("Component_"));
    }

    [Fact]
    public void GetRows_NoFilesNoComponentRef_ThrowsInvalidOperationException()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0" }
        };
        var package = new PackageModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = new Version(1, 0, 0)
        };
        var sut = new DependencyTableContributor(providers, []);

        Assert.Throws<InvalidOperationException>(() => sut.GetRows(CreateContext(package)));
    }

    [Fact]
    public void GetRows_FallsBackToFirstComponent()
    {
        var providers = new List<DependencyProviderModel>
        {
            new() { Key = "MyApp", Version = "1.0.0" }
        };
        var package = new PackageModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = new Version(1, 0, 0),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "app.exe",
                    TargetDirectory = KnownFolder.ProgramFiles / "TestApp",
                    FileName = "app.exe",
                    ComponentId = "comp_AppExe"
                }
            ]
        };
        var sut = new DependencyTableContributor(providers, []);

        var rows = sut.GetRows(CreateContext(package));

        Assert.Equal("comp_AppExe", rows[0].Get("Component_"));
        Assert.Equal("comp_AppExe", rows[1].Get("Component_"));
    }
}
