using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Iis.Tests;

public sealed class IisExtensionTests
{
    [Fact]
    public void Name_ReturnsIis()
    {
        var extension = new IisExtension();

        Assert.Equal("Iis", extension.Name);
    }

    [Fact]
    public void Extension_ImplementsIFalkForgeExtension()
    {
        var extension = new IisExtension();

        Assert.IsAssignableFrom<IFalkForgeExtension>(extension);
    }

    [Fact]
    public void AddWebSite_AddsToWebSitesList()
    {
        var extension = new IisExtension();
        extension.AddWebSite(site => site
            .Description("Test Site")
            .Directory("[INSTALLDIR]")
            .Binding(80));

        Assert.Single(extension.WebSites);
        Assert.Equal("Test Site", extension.WebSites[0].Description);
    }

    [Fact]
    public void AddAppPool_AddsToAppPoolsList()
    {
        var extension = new IisExtension();
        extension.AddAppPool(pool => pool.Name("TestPool"));

        Assert.Single(extension.AppPools);
        Assert.Equal("TestPool", extension.AppPools[0].Name);
    }

    [Fact]
    public void AddCertificate_AddsToCertificatesList()
    {
        var extension = new IisExtension();
        extension.AddCertificate(cert => cert.Id("cert1").FindByThumbprint("ABC123"));

        Assert.Single(extension.Certificates);
        Assert.Equal("cert1", extension.Certificates[0].Id);
    }

    [Fact]
    public void Validate_ValidConfiguration_ReturnsSuccess()
    {
        var extension = new IisExtension();
        extension.AddAppPool(pool => pool.Name("DefaultPool"));
        extension.AddWebSite(site => site
            .Description("My Site")
            .Directory("[INSTALLDIR]")
            .Binding(80)
            .AppPool("DefaultPool"));

        var result = extension.Validate();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_InvalidConfiguration_ReturnsFailure()
    {
        var extension = new IisExtension();
        extension.AddWebSite(site => site
            .Description("")
            .Directory("[INSTALLDIR]"));

        var result = extension.Validate();

        Assert.True(result.IsFailure);
        Assert.Contains("IIS001", result.Error.Message);
    }

    [Fact]
    public void Register_DoesNotThrow()
    {
        var extension = new IisExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        // IIS extension is model-only; Register is a no-op for now
    }

    [Fact]
    public void FluentChaining_AddMethods_ReturnExtension()
    {
        var extension = new IisExtension();
        var result = extension
            .AddAppPool(pool => pool.Name("Pool"))
            .AddCertificate(cert => cert.Id("cert1").FindByThumbprint("ABC"))
            .AddWebSite(site => site.Description("Site").Directory("[INSTALLDIR]").Binding(80));

        Assert.Same(extension, result);
    }

    private sealed class TestExtensionRegistry : IExtensionRegistry
    {
        public List<IMsiTableContributor> TableContributors { get; } = [];
        public List<IComponentContributor> ComponentContributors { get; } = [];
        public List<IExtensionValidator> Validators { get; } = [];

        public void RegisterTableContributor(IMsiTableContributor contributor) =>
            TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor) =>
            ComponentContributors.Add(contributor);

        public void RegisterValidator(IExtensionValidator validator) =>
            Validators.Add(validator);

        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
        public void RegisterDialogStep(IDialogStepBuilder builder) { }
    }
}
