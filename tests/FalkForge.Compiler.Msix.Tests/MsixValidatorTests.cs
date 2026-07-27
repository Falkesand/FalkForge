using FalkForge.Compiler.Msix.Builders;
using Xunit;

namespace FalkForge.Compiler.Msix.Tests;

public sealed class MsixValidatorTests
{
    private static MsixModel CreateValidModel() => new MsixBuilder()
        .Name("TestApp")
        .Publisher("CN=Test Publisher")
        .DisplayName("Test Application")
        .PublisherDisplayName("Test Publisher Inc.")
        .Version(new Version(1, 0, 0, 0))
        .Application("App1", "app.exe", app => app.DisplayName("Test App"))
        .Signing(s => s.Certificate("test.pfx"))
        .Build();

    [Fact]
    public void Validate_ValidModel_ReturnsSuccess()
    {
        var model = CreateValidModel();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyName_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX001", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyPublisher_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX002", result.Error.Message);
    }

    [Fact]
    public void Validate_PublisherWithoutCN_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX003", result.Error.Message);
    }

    [Fact]
    public void Validate_NoApplications_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX005", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyDisplayName_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX006", result.Error.Message);
    }

    [Fact]
    public void Validate_NoSigning_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX008", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyApplicationId_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX010", result.Error.Message);
    }

    [Fact]
    public void Validate_EmptyApplicationExecutable_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX011", result.Error.Message);
    }

    [Fact]
    public void Validate_InvalidMinWindowsVersion_ReturnsFailure()
    {
        var model = new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", app => app.DisplayName("Test App"))
            .Signing(s => s.Certificate("test.pfx"))
            .MinWindowsVersion("not-a-version")
            .Build();

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX012", result.Error.Message);
    }

    // Extension rules. These fail the build at authoring time instead of letting the package
    // reach MakeAppx/the Store, where the same mistakes surface as opaque deployment errors.

    private static MsixModel ModelWithApplication(Action<MsixApplicationBuilder> configure) =>
        new MsixBuilder()
            .Name("TestApp")
            .Publisher("CN=Test Publisher")
            .DisplayName("Test Application")
            .PublisherDisplayName("Test Publisher Inc.")
            .Version(new Version(1, 0, 0, 0))
            .Application("App1", "app.exe", configure)
            .Signing(s => s.Certificate("test.pfx"))
            .Build();

    [Fact]
    public void Validate_FileTypeAssociationNameWithUppercase_ReturnsFailure()
    {
        // uap:FileTypeAssociation/@Name is restricted to lowercase alphanumerics, '.', '-', '_'.
        var model = ModelWithApplication(app => app
            .DisplayName("Test App")
            .FileTypeAssociation("Contoso.Doc", ".cdoc"));

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX013", result.Error.Message);
    }

    [Fact]
    public void Validate_FileTypeWithoutLeadingDot_ReturnsFailure()
    {
        var model = ModelWithApplication(app => app
            .DisplayName("Test App")
            .FileTypeAssociation("contoso.doc", "cdoc"));

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX014", result.Error.Message);
    }

    [Fact]
    public void Validate_FileTypeAssociationWithNoFileTypes_ReturnsFailure()
    {
        // uap:SupportedFileTypes requires at least one uap:FileType child.
        var model = ModelWithApplication(app => app
            .DisplayName("Test App")
            .FileTypeAssociation("contoso.doc"));

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX014", result.Error.Message);
    }

    [Fact]
    public void Validate_ProtocolNameWithSchemeSeparator_ReturnsFailure()
    {
        // The Name is the scheme itself — "contoso", never "contoso://".
        var model = ModelWithApplication(app => app
            .DisplayName("Test App")
            .Protocol("contoso://"));

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("MSIX015", result.Error.Message);
    }

    [Fact]
    public void Validate_LowercaseAssociationAndProtocol_ReturnsSuccess()
    {
        var model = ModelWithApplication(app => app
            .DisplayName("Test App")
            .FileTypeAssociation("contoso.doc", ".cdoc")
            .Protocol("contoso"));

        var result = MsixValidator.Validate(model);

        Assert.True(result.IsSuccess);
    }
}
