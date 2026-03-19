using System.Xml.Linq;
using FalkForge.Compiler.Msix.Builders;
using FalkForge.Compiler.Msix.Manifest;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Manifest;

public sealed class AppxManifestGeneratorTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static MsixModel CreateValidModel() => new MsixBuilder()
        .Name("TestApp")
        .Publisher("CN=Test Publisher")
        .DisplayName("Test Application")
        .PublisherDisplayName("Test Publisher Inc.")
        .Version(new Version(1, 0, 0, 0))
        .Description("A test application")
        .LogoPath("Assets\\StoreLogo.png")
        .Application("App1", "app.exe", app => app.DisplayName("Test App"))
        .Capability("internetClient")
        .Signing(s => s.Certificate("test.pfx"))
        .Build();

    [Fact]
    public void Generate_MinimalModel_ProducesValidXml()
    {
        var model = CreateValidModel();

        var result = AppxManifestGenerator.Generate(model);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotNull(result.Value.Root);
        Assert.Equal(Ns + "Package", result.Value.Root!.Name);
    }

    [Fact]
    public void Generate_Identity_IncludesNamePublisherVersionArch()
    {
        var model = new MsixBuilder()
            .Name("MyApp")
            .Publisher("CN=Contoso")
            .DisplayName("My App")
            .PublisherDisplayName("Contoso")
            .Version(new Version(2, 3, 4, 0))
            .Architecture(ProcessorArchitecture.Arm64)
            .Application("App1", "app.exe", app => app.DisplayName("My App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var identity = result.Value.Root!.Element(Ns + "Identity")!;

        Assert.Equal("MyApp", identity.Attribute("Name")!.Value);
        Assert.Equal("CN=Contoso", identity.Attribute("Publisher")!.Value);
        Assert.Equal("2.3.4.0", identity.Attribute("Version")!.Value);
        Assert.Equal("arm64", identity.Attribute("ProcessorArchitecture")!.Value);
    }

    [Fact]
    public void Generate_Properties_IncludesDisplayNameAndLogo()
    {
        var model = CreateValidModel();

        var result = AppxManifestGenerator.Generate(model);
        var props = result.Value.Root!.Element(Ns + "Properties")!;

        Assert.Equal("Test Application", props.Element(Ns + "DisplayName")!.Value);
        Assert.Equal("Test Publisher Inc.", props.Element(Ns + "PublisherDisplayName")!.Value);
        Assert.Equal("A test application", props.Element(Ns + "Description")!.Value);
        Assert.Equal("Assets\\StoreLogo.png", props.Element(Ns + "Logo")!.Value);
    }

    [Fact]
    public void Generate_Dependencies_IncludesTargetDeviceFamily()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .MinWindowsVersion("10.0.19041.0")
            .Application("App1", "app.exe", app => app.DisplayName("Test"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var deps = result.Value.Root!.Element(Ns + "Dependencies")!;
        var tdf = deps.Element(Ns + "TargetDeviceFamily")!;

        Assert.Equal("Windows.Desktop", tdf.Attribute("Name")!.Value);
        Assert.Equal("10.0.19041.0", tdf.Attribute("MinVersion")!.Value);
        Assert.Equal("10.0.26100.0", tdf.Attribute("MaxVersionTested")!.Value);
    }

    [Fact]
    public void Generate_Capabilities_IncludesGeneralCapabilities()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test"))
            .Capability("internetClient")
            .Capability("privateNetworkClientServer")
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var caps = result.Value.Root!.Element(Ns + "Capabilities")!;
        var capElements = caps.Elements(Ns + "Capability").ToList();

        Assert.Equal(2, capElements.Count);
        Assert.Equal("internetClient", capElements[0].Attribute("Name")!.Value);
        Assert.Equal("privateNetworkClientServer", capElements[1].Attribute("Name")!.Value);
    }

    [Fact]
    public void Generate_RestrictedCapabilities_IncludesRescap()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test"))
            .RestrictedCapability("runFullTrust")
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var caps = result.Value.Root!.Element(Ns + "Capabilities")!;
        var rescapElements = caps.Elements(Rescap + "Capability").ToList();

        Assert.Single(rescapElements);
        Assert.Equal("runFullTrust", rescapElements[0].Attribute("Name")!.Value);
    }

    [Fact]
    public void Generate_SingleApplication_IncludesAppElement()
    {
        var model = CreateValidModel();

        var result = AppxManifestGenerator.Generate(model);
        var apps = result.Value.Root!.Element(Ns + "Applications")!;
        var appElements = apps.Elements(Ns + "Application").ToList();

        Assert.Single(appElements);
        Assert.Equal("App1", appElements[0].Attribute("Id")!.Value);
        Assert.Equal("app.exe", appElements[0].Attribute("Executable")!.Value);
    }

    [Fact]
    public void Generate_MultipleApplications_IncludesAll()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "first.exe", app => app.DisplayName("First"))
            .Application("App2", "second.exe", app => app.DisplayName("Second"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var apps = result.Value.Root!.Element(Ns + "Applications")!;
        var appElements = apps.Elements(Ns + "Application").ToList();

        Assert.Equal(2, appElements.Count);
        Assert.Equal("App1", appElements[0].Attribute("Id")!.Value);
        Assert.Equal("App2", appElements[1].Attribute("Id")!.Value);
    }

    [Fact]
    public void Generate_VisualElements_IncludesDisplayNameAndColor()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app
                .DisplayName("My Visual App")
                .BackgroundColor("#FF0000")
                .Square150x150Logo("Assets\\Logo150.png")
                .Square44x44Logo("Assets\\Logo44.png"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var apps = result.Value.Root!.Element(Ns + "Applications")!;
        var appEl = apps.Element(Ns + "Application")!;
        var visual = appEl.Element(Uap + "VisualElements")!;

        Assert.Equal("My Visual App", visual.Attribute("DisplayName")!.Value);
        Assert.Equal("#FF0000", visual.Attribute("BackgroundColor")!.Value);
        Assert.Equal("Assets\\Logo150.png", visual.Attribute("Square150x150Logo")!.Value);
        Assert.Equal("Assets\\Logo44.png", visual.Attribute("Square44x44Logo")!.Value);
    }

    [Fact]
    public void Generate_PackageDependencies_IncludesDependencyElements()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test")
            .DisplayName("Test")
            .PublisherDisplayName("Test")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test"))
            .Dependency("Microsoft.VCLibs.140.00", "CN=Microsoft", new Version(14, 0, 30704, 0))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = AppxManifestGenerator.Generate(model);
        var deps = result.Value.Root!.Element(Ns + "Dependencies")!;
        var pkgDeps = deps.Elements(Ns + "PackageDependency").ToList();

        Assert.Single(pkgDeps);
        Assert.Equal("Microsoft.VCLibs.140.00", pkgDeps[0].Attribute("Name")!.Value);
        Assert.Equal("CN=Microsoft", pkgDeps[0].Attribute("Publisher")!.Value);
        Assert.Equal("14.0.30704.0", pkgDeps[0].Attribute("MinVersion")!.Value);
    }
}
