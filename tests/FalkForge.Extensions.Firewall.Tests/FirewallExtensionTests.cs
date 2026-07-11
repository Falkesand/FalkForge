using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Extensions.Firewall.Tests;

public sealed class FirewallExtensionTests
{
    [Fact]
    public void Name_ReturnsFirewall()
    {
        var extension = new FirewallExtension();

        Assert.Equal("Firewall", extension.Name);
    }

    [Fact]
    public void Register_RegistersTableContributor()
    {
        var extension = new FirewallExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Single(registry.TableContributors);
        Assert.IsType<FirewallTableContributor>(registry.TableContributors[0]);
    }

    [Fact]
    public void Register_RegistersExecutionContributor()
    {
        var extension = new FirewallExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Single(registry.ExecutionContributors);
    }

    [Fact]
    public void Register_TableContributor_HasCorrectTableName()
    {
        var extension = new FirewallExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Equal("WixFirewallException", registry.TableContributors[0].TableName);
    }

    [Fact]
    public void Extension_ImplementsIFalkForgeExtension()
    {
        var extension = new FirewallExtension();

        Assert.IsAssignableFrom<IFalkForgeExtension>(extension);
    }

    [Fact]
    public void AddRule_AddsRuleToTableContributor()
    {
        var extension = new FirewallExtension();

        extension.AddRule(r => r
            .Id("FW1")
            .Name("HTTP")
            .Port("80"));

        Assert.Single(extension.TableContributor.Rules);
        Assert.Equal("HTTP", extension.TableContributor.Rules[0].Name);
    }

    [Fact]
    public void GetValidationRules_ReturnsErrorsForInvalidRules()
    {
        var extension = new FirewallExtension();
        extension.AddRule(r => r.Id("FW_Bad").Name("").Port("80"));

        var rules = extension.GetValidationRules();
        var engine = new ValidationEngine(new RuleRegistry(rules));
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add("app.exe").To(KnownFolder.ProgramFiles / "Corp" / "App"));
        });

        var report = engine.Run(package);

        Assert.NotEmpty(report.Violations);
        Assert.Contains(report.Violations, v => v.RuleId.Value == "FWL001");
    }

    [Fact]
    public void GetValidationRules_ReturnsFourRules()
    {
        var extension = new FirewallExtension();
        var rules = extension.GetValidationRules();
        Assert.Equal(4, rules.Length);
    }

    private sealed class TestExtensionRegistry : IExtensionRegistry
    {
        public List<IMsiTableContributor> TableContributors { get; } = [];
        public List<IComponentContributor> ComponentContributors { get; } = [];
        public List<IExecutionContributor> ExecutionContributors { get; } = [];

        public void RegisterTableContributor(IMsiTableContributor contributor) =>
            TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor) =>
            ComponentContributors.Add(contributor);

        public void RegisterExecutionContributor(IExecutionContributor contributor) =>
            ExecutionContributors.Add(contributor);

        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
        public void RegisterDialogStep(IDialogStepBuilder builder) { }
    }
}
