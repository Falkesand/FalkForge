using FalkForge.Compiler.Msix.Builders;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests.Builders;

public sealed class MsixBuilderTests
{
    [Fact]
    public void Build_MinimalModel_SetsRequiredFields()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Version(new Version(2, 0, 0, 0))
            .Application("App", "MyApp.exe", app => app
                .DisplayName("My App"))
            .Build();

        Assert.Equal("MyCompany.MyApp", model.Name);
        Assert.Equal("CN=MyCompany", model.Publisher);
        Assert.Equal("My Application", model.DisplayName);
        Assert.Equal("My Company", model.PublisherDisplayName);
        Assert.Equal(new Version(2, 0, 0, 0), model.Version);
        Assert.Single(model.Applications);
        Assert.Equal("App", model.Applications[0].Id);
        Assert.Equal("MyApp.exe", model.Applications[0].Executable);
    }

    [Fact]
    public void Build_SetsArchitecture()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Architecture(ProcessorArchitecture.Arm64)
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .Build();

        Assert.Equal(ProcessorArchitecture.Arm64, model.Architecture);
    }

    [Fact]
    public void Build_AddFiles_IncludedInModel()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .Files(files => files
                .Add("C:\\temp\\app.exe")
                .To(FalkForge.KnownFolder.ProgramFiles / "MyApp"))
            .Build();

        Assert.Single(model.Files);
        Assert.Equal("app.exe", model.Files[0].FileName);
    }

    [Fact]
    public void Build_AddCapabilities_IncludedInModel()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .Capability("internetClient")
            .Capability("privateNetworkClientServer")
            .Build();

        Assert.Equal(2, model.Capabilities.Count);
        Assert.Contains("internetClient", model.Capabilities);
        Assert.Contains("privateNetworkClientServer", model.Capabilities);
    }

    [Fact]
    public void Build_AddRestrictedCapabilities_IncludedInModel()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .RestrictedCapability("runFullTrust")
            .RestrictedCapability("allowElevation")
            .Build();

        Assert.Equal(2, model.RestrictedCapabilities.Count);
        Assert.Contains("runFullTrust", model.RestrictedCapabilities);
        Assert.Contains("allowElevation", model.RestrictedCapabilities);
    }

    [Fact]
    public void Build_MultipleApplications_AllIncluded()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App1", "App1.exe", app => app
                .DisplayName("First App")
                .Description("The first application")
                .BackgroundColor("#FF0000"))
            .Application("App2", "App2.exe", app => app
                .DisplayName("Second App")
                .Square150x150Logo("Assets\\Logo150.png"))
            .Build();

        Assert.Equal(2, model.Applications.Count);
        Assert.Equal("App1", model.Applications[0].Id);
        Assert.Equal("First App", model.Applications[0].VisualElements.DisplayName);
        Assert.Equal("The first application", model.Applications[0].VisualElements.Description);
        Assert.Equal("#FF0000", model.Applications[0].VisualElements.BackgroundColor);
        Assert.Equal("App2", model.Applications[1].Id);
        Assert.Equal("Second App", model.Applications[1].VisualElements.DisplayName);
        Assert.Equal("Assets\\Logo150.png", model.Applications[1].VisualElements.Square150x150Logo);
    }

    [Fact]
    public void Build_SigningOptions_SetCorrectly()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .Signing(sign => sign
                .Certificate("cert.pfx")
                .Timestamp("http://timestamp.digicert.com")
                .Algorithm("sha256"))
            .Build();

        Assert.NotNull(model.Signing);
        Assert.Equal("cert.pfx", model.Signing.CertificatePath);
        Assert.Equal("http://timestamp.digicert.com", model.Signing.TimestampUrl);
        Assert.Equal("sha256", model.Signing.DigestAlgorithm);
    }

    [Fact]
    public void Build_UpdateSettings_SetCorrectly()
    {
        var model = new MsixBuilder()
            .Name("MyCompany.MyApp")
            .Publisher("CN=MyCompany")
            .DisplayName("My Application")
            .PublisherDisplayName("My Company")
            .Application("App", "MyApp.exe", app => app.DisplayName("App"))
            .UpdateSettings("https://example.com/app.appinstaller", update => update
                .HoursBetweenUpdateChecks(12)
                .ShowPrompt()
                .AutomaticBackgroundTask())
            .Build();

        Assert.NotNull(model.UpdateSettings);
        Assert.Equal("https://example.com/app.appinstaller", model.UpdateSettings.AppInstallerUri);
        Assert.Equal(12, model.UpdateSettings.HoursBetweenUpdateChecks);
        Assert.True(model.UpdateSettings.ShowPrompt);
        Assert.True(model.UpdateSettings.AutomaticBackgroundTask);
        Assert.False(model.UpdateSettings.UpdateBlocksActivation);
        Assert.False(model.UpdateSettings.ForceUpdateFromAnyVersion);
    }
}
