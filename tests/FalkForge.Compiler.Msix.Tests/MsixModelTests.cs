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
        Assert.Empty(model.Capabilities);
        Assert.Empty(model.RestrictedCapabilities);
        Assert.Empty(model.Dependencies);
        Assert.Empty(model.VfsOverrides);
    }

    /// <summary>
    /// MSIX cannot express these concepts, so the model must not offer them: a property the
    /// fluent API accepts and the compiler cannot honour is worse than no property at all —
    /// the caller believes the setting took effect.
    /// <list type="bullet">
    /// <item><c>Scope</c> — an MSIX package is always staged and registered per-user;
    /// making it available to every user is a deployment-time provisioning act
    /// (<c>Add-AppxProvisionedPackage</c>), with no AppxManifest representation.</item>
    /// <item><c>Shortcuts</c> — Start menu entries come from <c>Applications/Application</c>
    /// plus its VisualElements; AppxManifest has no shortcut element.</item>
    /// <item><c>Extensions</c> — replaced by per-application, schema-correct
    /// <c>FileTypeAssociations</c> / <c>Protocols</c>; a bare category + entry point cannot
    /// select the right XML namespace or emit the children each category requires.</item>
    /// </list>
    /// </summary>
    [Theory]
    [InlineData("Scope")]
    [InlineData("Shortcuts")]
    [InlineData("Extensions")]
    public void MsixModel_DoesNotExposeConceptsMsixCannotRepresent(string propertyName)
    {
        Assert.Null(typeof(MsixModel).GetProperty(propertyName));
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
