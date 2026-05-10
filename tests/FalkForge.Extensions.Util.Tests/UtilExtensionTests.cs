using FalkForge.Extensibility;
using FalkForge.Extensions.Util.Odbc;
using FalkForge.Extensions.Util.PerfCounter;
using FalkForge.Extensions.Util.ScheduledTask;
using FalkForge.Extensions.Util.XmlConfig;
using Xunit;

namespace FalkForge.Extensions.Util.Tests;

public sealed class UtilExtensionTests
{
    [Fact]
    public void Name_ReturnsUtil()
    {
        var extension = new UtilExtension();

        Assert.Equal("Util", extension.Name);
    }

    [Fact]
    public void Extension_ImplementsIFalkForgeExtension()
    {
        var extension = new UtilExtension();

        Assert.IsAssignableFrom<IFalkForgeExtension>(extension);
    }

    [Fact]
    public void Register_RegistersXmlConfigTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is XmlConfigTableContributor);
    }

    [Fact]
    public void Register_RegistersScheduledTaskTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is ScheduledTaskTableContributor);
    }

    [Fact]
    public void Register_XmlConfigContributor_HasCorrectTableName()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        var xmlContributor = registry.TableContributors.First(c => c is XmlConfigTableContributor);
        Assert.Equal("XmlConfig", xmlContributor.TableName);
    }

    [Fact]
    public void XmlConfig_ReturnsTableContributor()
    {
        var extension = new UtilExtension();

        Assert.NotNull(extension.XmlConfig);
        Assert.IsType<XmlConfigTableContributor>(extension.XmlConfig);
    }

    [Fact]
    public void XmlConfig_ReturnsSameInstanceAcrossCalls()
    {
        var extension = new UtilExtension();

        var first = extension.XmlConfig;
        var second = extension.XmlConfig;

        Assert.Same(first, second);
    }

    [Fact]
    public void Register_RegistersPerfCounterTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is PerfCounterTableContributor);
    }

    [Fact]
    public void Register_RegistersOdbcDriverTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is OdbcDriverTableContributor);
    }

    [Fact]
    public void Register_RegistersOdbcDataSourceTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Contains(registry.TableContributors, c => c is OdbcDataSourceTableContributor);
    }

    [Fact]
    public void Register_DoesNotRegisterComponentContributors()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Empty(registry.ComponentContributors);
    }

    private sealed class TestExtensionRegistry : IExtensionRegistry
    {
        public List<IMsiTableContributor> TableContributors { get; } = [];
        public List<IComponentContributor> ComponentContributors { get; } = [];

        public void RegisterTableContributor(IMsiTableContributor contributor) =>
            TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor) =>
            ComponentContributors.Add(contributor);

        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
        public void RegisterDialogStep(IDialogStepBuilder builder) { }
    }
}
