using FalkForge.Extensibility;
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
    public void ValidateRules_ReturnsErrorsForInvalidRules()
    {
        var extension = new FirewallExtension();
        extension.AddRule(r => r.Id("FW_Bad").Name(""));

        var errors = extension.ValidateRules();

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Code == "FWL001");
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
    }
}
