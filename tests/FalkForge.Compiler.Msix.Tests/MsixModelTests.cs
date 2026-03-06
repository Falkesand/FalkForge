using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

public sealed class MsixModelTests
{
    private static MsixApplication CreateApplication(string id = "App") => new()
    {
        Id = id,
        Executable = "MyApp.exe",
        VisualElements = new MsixVisualElements { DisplayName = "My App" }
    };

    [Fact]
    public void MsixModel_RequiredProperties_CanBeConstructed()
    {
        var model = new MsixModel
        {
            Name = "MyCompany.MyApp",
            Publisher = "CN=MyCompany",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "My Application",
            PublisherDisplayName = "My Company",
            Applications = [CreateApplication()]
        };

        Assert.Equal("MyCompany.MyApp", model.Name);
        Assert.Equal("CN=MyCompany", model.Publisher);
        Assert.Equal(new Version(1, 0, 0, 0), model.Version);
        Assert.Equal("My Application", model.DisplayName);
        Assert.Equal("My Company", model.PublisherDisplayName);
        Assert.Single(model.Applications);
    }

    [Fact]
    public void MsixModel_DefaultValues_AreCorrect()
    {
        var model = new MsixModel
        {
            Name = "MyCompany.MyApp",
            Publisher = "CN=MyCompany",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "My Application",
            PublisherDisplayName = "My Company",
            Applications = [CreateApplication()]
        };

        Assert.Equal(ProcessorArchitecture.X64, model.Architecture);
        Assert.Equal(InstallScope.PerMachine, model.Scope);
        Assert.Equal("10.0.17763.0", model.MinWindowsVersion);
        Assert.Equal(VfsMappingMode.Auto, model.VfsMapping);
    }

    [Fact]
    public void MsixModel_OptionalCollections_DefaultToEmpty()
    {
        var model = new MsixModel
        {
            Name = "MyCompany.MyApp",
            Publisher = "CN=MyCompany",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "My Application",
            PublisherDisplayName = "My Company",
            Applications = [CreateApplication()]
        };

        Assert.Empty(model.Files);
        Assert.Empty(model.RegistryEntries);
        Assert.Empty(model.Shortcuts);
        Assert.Empty(model.Capabilities);
        Assert.Empty(model.RestrictedCapabilities);
        Assert.Empty(model.Dependencies);
        Assert.Empty(model.Extensions);
        Assert.Empty(model.VfsOverrides);
    }

    [Fact]
    public void MsixModel_WithApplications_StoresCorrectly()
    {
        var app1 = CreateApplication("App1");
        var app2 = CreateApplication("App2");

        var model = new MsixModel
        {
            Name = "MyCompany.MyApp",
            Publisher = "CN=MyCompany",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "My Application",
            PublisherDisplayName = "My Company",
            Applications = [app1, app2]
        };

        Assert.Equal(2, model.Applications.Count);
        Assert.Equal("App1", model.Applications[0].Id);
        Assert.Equal("App2", model.Applications[1].Id);
        Assert.Equal("MyApp.exe", model.Applications[0].Executable);
        Assert.Equal("My App", model.Applications[0].VisualElements.DisplayName);
    }

    [Fact]
    public void MsixModel_WithCapabilities_StoresCorrectly()
    {
        var model = new MsixModel
        {
            Name = "MyCompany.MyApp",
            Publisher = "CN=MyCompany",
            Version = new Version(1, 0, 0, 0),
            DisplayName = "My Application",
            PublisherDisplayName = "My Company",
            Applications = [CreateApplication()],
            Capabilities = ["internetClient", "privateNetworkClientServer"],
            RestrictedCapabilities = ["runFullTrust"]
        };

        Assert.Equal(2, model.Capabilities.Count);
        Assert.Contains("internetClient", model.Capabilities);
        Assert.Contains("privateNetworkClientServer", model.Capabilities);
        Assert.Single(model.RestrictedCapabilities);
        Assert.Contains("runFullTrust", model.RestrictedCapabilities);
    }
}
