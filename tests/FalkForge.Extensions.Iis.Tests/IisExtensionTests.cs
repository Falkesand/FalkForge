using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
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
    public void GetValidationRules_ValidConfiguration_ProducesNoViolations()
    {
        var extension = new IisExtension();
        extension.AddAppPool(pool => pool.Name("DefaultPool"));
        extension.AddWebSite(site => site
            .Description("My Site")
            .Directory("[INSTALLDIR]")
            .Binding(80)
            .AppPool("DefaultPool"));

        var engine = new ValidationEngine(new RuleRegistry(extension.GetValidationRules()));
        var package = MinimalPackage();

        var report = engine.Run(package);

        Assert.True(report.IsValid);
    }

    [Fact]
    public void GetValidationRules_InvalidConfiguration_ProducesIIS001Violation()
    {
        var extension = new IisExtension();
        extension.AddWebSite(site => site
            .Description("")
            .Directory("[INSTALLDIR]"));

        var engine = new ValidationEngine(new RuleRegistry(extension.GetValidationRules()));
        var package = MinimalPackage();

        var report = engine.Run(package);

        Assert.False(report.IsValid);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "IIS001");
    }

    [Fact]
    public void GetValidationRules_ReturnsElevenRules()
    {
        var extension = new IisExtension();
        Assert.Equal(11, extension.GetValidationRules().Length);
    }

    private static PackageModel MinimalPackage() => InstallerTestHost.BuildPackage(p =>
    {
        p.Name = "App";
        p.Manufacturer = "Corp";
        p.Version = new Version(1, 0, 0);
        p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
    });

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
