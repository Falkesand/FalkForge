using System.Xml.Linq;
using FalkForge.Studio.Import;
using Xunit;

namespace FalkForge.Studio.Tests.Import;

public sealed class WixImporterTests
{
    private static readonly XNamespace WixV3 = "http://schemas.microsoft.com/wix/2006/wi";
    private static readonly XNamespace WixV4 = "http://wixtoolset.org/schemas/v4/wxs";

    [Fact]
    public void Import_MinimalWixV3Product_MapsProductSection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "Test App"),
                    new XAttribute("Manufacturer", "Test Corp"),
                    new XAttribute("Version", "2.1.0"),
                    new XAttribute("UpgradeCode", "12345678-1234-1234-1234-123456789012"),
                    new XAttribute("Language", "1033"),
                    new XElement(WixV3 + "Package",
                        new XAttribute("Description", "A test installer"),
                        new XAttribute("Platform", "x64"),
                        new XAttribute("InstallScope", "perMachine")))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        var project = result.Value;
        Assert.Equal("Test App", project.Product.Name);
        Assert.Equal("Test Corp", project.Product.Manufacturer);
        Assert.Equal("2.1.0", project.Product.Version);
        Assert.Equal("12345678-1234-1234-1234-123456789012", project.Product.UpgradeCode);
        Assert.Equal("A test installer", project.Product.Description);
        Assert.Equal("x64", project.Product.Architecture);
        Assert.Equal("perMachine", project.Product.Scope);
    }

    [Fact]
    public void Import_MinimalWixV4Package_MapsProductSection()
    {
        var doc = new XDocument(
            new XElement(WixV4 + "Wix",
                new XElement(WixV4 + "Package",
                    new XAttribute("Name", "V4 App"),
                    new XAttribute("Manufacturer", "V4 Corp"),
                    new XAttribute("Version", "3.0.0"),
                    new XAttribute("UpgradeCode", "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
                    new XAttribute("Scope", "perUser"))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        var project = result.Value;
        Assert.Equal("V4 App", project.Product.Name);
        Assert.Equal("V4 Corp", project.Product.Manufacturer);
        Assert.Equal("3.0.0", project.Product.Version);
        Assert.Equal("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", project.Product.UpgradeCode);
        Assert.Equal("perUser", project.Product.Scope);
    }

    [Fact]
    public void Import_FeatureTreeWithFiles_MapsFeatureHierarchy()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "MainExe"),
                            new XElement(WixV3 + "File",
                                new XAttribute("Source", "app.exe"),
                                new XAttribute("Vital", "yes"))),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "PluginDll"),
                            new XElement(WixV3 + "File",
                                new XAttribute("Source", "plugin.dll"),
                                new XAttribute("Vital", "no")))),
                    new XElement(WixV3 + "Feature",
                        new XAttribute("Id", "MainFeature"),
                        new XAttribute("Title", "Main"),
                        new XAttribute("Level", "1"),
                        new XElement(WixV3 + "ComponentRef",
                            new XAttribute("Id", "MainExe")),
                        new XElement(WixV3 + "Feature",
                            new XAttribute("Id", "PluginFeature"),
                            new XAttribute("Title", "Plugins"),
                            new XAttribute("Level", "2"),
                            new XAttribute("Absent", "disallow"),
                            new XElement(WixV3 + "ComponentRef",
                                new XAttribute("Id", "PluginDll")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        var project = result.Value;
        Assert.Single(project.Features);

        var main = project.Features[0];
        Assert.Equal("MainFeature", main.Id);
        Assert.Equal("Main", main.Title);
        Assert.Single(main.Files);
        Assert.Equal("app.exe", main.Files[0].Source);
        Assert.True(main.Files[0].Vital);

        Assert.NotNull(main.Features);
        Assert.Single(main.Features);
        var plugin = main.Features[0];
        Assert.Equal("PluginFeature", plugin.Id);
        Assert.Equal("Plugins", plugin.Title);
        Assert.True(plugin.IsRequired);
        Assert.Equal(2, plugin.InstallLevel);
        Assert.Single(plugin.Files);
        Assert.Equal("plugin.dll", plugin.Files[0].Source);
        Assert.False(plugin.Files[0].Vital);
    }

    [Fact]
    public void Import_RegistryEntries_MapsRegistrySection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "RegComp"),
                            new XElement(WixV3 + "RegistryKey",
                                new XAttribute("Root", "HKLM"),
                                new XAttribute("Key", @"SOFTWARE\TestApp"),
                                new XElement(WixV3 + "RegistryValue",
                                    new XAttribute("Name", "InstallPath"),
                                    new XAttribute("Type", "string"),
                                    new XAttribute("Value", "[INSTALLDIR]")),
                                new XElement(WixV3 + "RegistryValue",
                                    new XAttribute("Name", "MajorVersion"),
                                    new XAttribute("Type", "integer"),
                                    new XAttribute("Value", "1"))))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Registry.Count);
        Assert.Equal("LocalMachine", result.Value.Registry[0].Root);
        Assert.Equal(@"SOFTWARE\TestApp", result.Value.Registry[0].Key);
        Assert.Equal("InstallPath", result.Value.Registry[0].ValueName);
        Assert.Equal("String", result.Value.Registry[0].ValueType);
        Assert.Equal("[INSTALLDIR]", result.Value.Registry[0].Value);

        Assert.Equal("MajorVersion", result.Value.Registry[1].ValueName);
        Assert.Equal("DWord", result.Value.Registry[1].ValueType);
    }

    [Fact]
    public void Import_ServiceInstall_MapsServiceSection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "SvcComp"),
                            new XElement(WixV3 + "ServiceInstall",
                                new XAttribute("Id", "MySvc"),
                                new XAttribute("Name", "TestService"),
                                new XAttribute("DisplayName", "Test Service"),
                                new XAttribute("Description", "A test service"),
                                new XAttribute("Start", "demand"),
                                new XAttribute("Account", "LocalService")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Services);
        var svc = result.Value.Services[0];
        Assert.Equal("TestService", svc.Name);
        Assert.Equal("Test Service", svc.DisplayName);
        Assert.Equal("A test service", svc.Description);
        Assert.Equal("Manual", svc.StartMode);
        Assert.Equal("LocalService", svc.Account);
    }

    [Fact]
    public void Import_EnvironmentVariables_MapsEnvironmentSection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "EnvComp"),
                            new XElement(WixV3 + "Environment",
                                new XAttribute("Name", "MY_VAR"),
                                new XAttribute("Value", "hello"),
                                new XAttribute("Action", "set"),
                                new XAttribute("System", "yes")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Environment);
        Assert.Equal("MY_VAR", result.Value.Environment[0].Name);
        Assert.Equal("hello", result.Value.Environment[0].Value);
        Assert.Equal("Set", result.Value.Environment[0].Action);
        Assert.True(result.Value.Environment[0].IsSystem);
    }

    [Fact]
    public void Import_CustomActions_MapsCustomActionSection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "CustomAction",
                        new XAttribute("Id", "SetInstallDir"),
                        new XAttribute("Property", "INSTALLDIR"),
                        new XAttribute("Value", "[ProgramFilesFolder]App")),
                    new XElement(WixV3 + "CustomAction",
                        new XAttribute("Id", "RunSetup"),
                        new XAttribute("BinaryKey", "SetupDll"),
                        new XAttribute("DllEntry", "Configure"),
                        new XAttribute("Execute", "deferred"),
                        new XAttribute("Impersonate", "no"),
                        new XAttribute("Return", "ignore")))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.CustomActions.Count);

        var setProp = result.Value.CustomActions[0];
        Assert.Equal("SetInstallDir", setProp.Id);
        Assert.Equal("SetProperty", setProp.Type);
        Assert.Equal("INSTALLDIR", setProp.Source);
        Assert.Equal("[ProgramFilesFolder]App", setProp.Target);

        var runSetup = result.Value.CustomActions[1];
        Assert.Equal("RunSetup", runSetup.Id);
        Assert.Equal("DllFromBinary", runSetup.Type);
        Assert.Equal("SetupDll", runSetup.Source);
        Assert.Equal("Configure", runSetup.Target);
        Assert.True(runSetup.Deferred);
        Assert.True(runSetup.NoImpersonate);
        Assert.True(runSetup.ContinueOnError);
    }

    [Fact]
    public void Import_UnknownElements_DoesNotCrash()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "SomeUnknownElement",
                        new XAttribute("Id", "unknown")),
                    new XElement(WixV3 + "Property",
                        new XAttribute("Id", "WIXUI_INSTALLDIR"),
                        new XAttribute("Value", "INSTALLDIR")))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Equal("App", result.Value.Product.Name);
    }

    [Fact]
    public void Import_InvalidXml_ReturnsFailure()
    {
        var doc = new XDocument(new XElement("NotAWixFile"));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsFailure);
        Assert.Contains("Not a recognized WiX source file", result.Error.Message);
    }

    [Fact]
    public void Import_Shortcuts_MapsShortcutSection()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "ShortcutComp"),
                            new XElement(WixV3 + "Shortcut",
                                new XAttribute("Name", "My App"),
                                new XAttribute("Target", "[INSTALLDIR]app.exe"),
                                new XAttribute("Directory", "DesktopFolder"),
                                new XAttribute("Description", "Launch My App")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
        var shortcut = result.Value.Shortcuts[0];
        Assert.Equal("My App", shortcut.Name);
        Assert.Equal("[INSTALLDIR]app.exe", shortcut.TargetFile);
        Assert.True(shortcut.Desktop);
        Assert.Equal("Launch My App", shortcut.Description);
    }

    [Fact]
    public void Import_InstallDirectory_ResolvesFromDirectoryTree()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Directory",
                            new XAttribute("Id", "ProgramFilesFolder"),
                            new XElement(WixV3 + "Directory",
                                new XAttribute("Id", "INSTALLDIR"),
                                new XAttribute("Name", "My Application")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Equal("My Application", result.Value.InstallDirectory);
    }

    [Fact]
    public void Import_WixV4RegistryValueDirectInComponent_MapsRegistrySection()
    {
        var doc = new XDocument(
            new XElement(WixV4 + "Wix",
                new XElement(WixV4 + "Package",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV4 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV4 + "Component",
                            new XAttribute("Id", "RegComp"),
                            new XElement(WixV4 + "RegistryValue",
                                new XAttribute("Root", "HKCU"),
                                new XAttribute("Key", @"SOFTWARE\V4App"),
                                new XAttribute("Name", "Setting"),
                                new XAttribute("Type", "string"),
                                new XAttribute("Value", "enabled")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Registry);
        Assert.Equal("CurrentUser", result.Value.Registry[0].Root);
        Assert.Equal(@"SOFTWARE\V4App", result.Value.Registry[0].Key);
        Assert.Equal("Setting", result.Value.Registry[0].ValueName);
    }

    [Fact]
    public void Import_NoFeaturesButHasFiles_CreatesDefaultFeature()
    {
        var doc = new XDocument(
            new XElement(WixV3 + "Wix",
                new XElement(WixV3 + "Product",
                    new XAttribute("Name", "App"),
                    new XAttribute("Manufacturer", "Corp"),
                    new XAttribute("Version", "1.0.0"),
                    new XElement(WixV3 + "Directory",
                        new XAttribute("Id", "TARGETDIR"),
                        new XElement(WixV3 + "Component",
                            new XAttribute("Id", "MainComp"),
                            new XElement(WixV3 + "File",
                                new XAttribute("Source", "app.exe")))))));

        var result = WixImporter.ImportFromDocument(doc);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features);
        Assert.Equal("DefaultFeature", result.Value.Features[0].Id);
        Assert.Single(result.Value.Features[0].Files);
        Assert.Equal("app.exe", result.Value.Features[0].Files[0].Source);
    }
}
