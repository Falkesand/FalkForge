using System.Xml.Linq;
using FalkForge.Compiler.Msix.Builders;
using FalkForge.Compiler.Msix.Manifest;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Manifest;

public sealed class AppInstallerGeneratorTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/appinstaller/2021";

    private static MsixModel CreateModelWithUpdateSettings(Action<MsixUpdateSettingsBuilder>? configure = null) =>
        new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 2, 3, 0))
            .Architecture(ProcessorArchitecture.X64)
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .UpdateSettings("https://example.com/releases/TestApp.appinstaller", configure)
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

    [Fact]
    public void Generate_WithUpdateSettings_ProducesValidXml()
    {
        var model = CreateModelWithUpdateSettings();

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.Root);
        Assert.Equal(Ns + "AppInstaller", result.Value.Root!.Name);
    }

    [Fact]
    public void Generate_NoUpdateSettings_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorKind.InvalidConfiguration, result.Error.Kind);
    }

    [Fact]
    public void Generate_IncludesMainPackageAttributes()
    {
        var model = CreateModelWithUpdateSettings();

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");
        var mainPackage = result.Value.Root!.Element(Ns + "MainPackage")!;

        Assert.Equal("TestApp", mainPackage.Attribute("Name")!.Value);
        Assert.Equal("CN=Test Publisher", mainPackage.Attribute("Publisher")!.Value);
        Assert.Equal("1.2.3.0", mainPackage.Attribute("Version")!.Value);
        Assert.Equal("x64", mainPackage.Attribute("ProcessorArchitecture")!.Value);
        Assert.Equal("https://example.com/releases/TestApp.msix", mainPackage.Attribute("Uri")!.Value);
    }

    [Fact]
    public void Generate_IncludesOnLaunchSettings()
    {
        var model = CreateModelWithUpdateSettings(s => s.HoursBetweenUpdateChecks(12));

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");
        var updateSettings = result.Value.Root!.Element(Ns + "UpdateSettings")!;
        var onLaunch = updateSettings.Element(Ns + "OnLaunch")!;

        Assert.Equal("12", onLaunch.Attribute("HoursBetweenUpdateChecks")!.Value);
    }

    [Fact]
    public void Generate_AutomaticBackgroundTask_Included()
    {
        var model = CreateModelWithUpdateSettings(s => s.AutomaticBackgroundTask());

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");
        var updateSettings = result.Value.Root!.Element(Ns + "UpdateSettings")!;
        var bgTask = updateSettings.Element(Ns + "AutomaticBackgroundTask");

        Assert.NotNull(bgTask);
    }

    [Fact]
    public void Generate_ForceUpdateFromAnyVersion_Included()
    {
        var model = CreateModelWithUpdateSettings(s => s.ForceUpdateFromAnyVersion());

        var result = AppInstallerGenerator.Generate(model, "TestApp.msix");
        var updateSettings = result.Value.Root!.Element(Ns + "UpdateSettings")!;
        var forceUpdate = updateSettings.Element(Ns + "ForceUpdateFromAnyVersion");

        Assert.NotNull(forceUpdate);
        Assert.Equal("true", forceUpdate!.Value);
    }
}
