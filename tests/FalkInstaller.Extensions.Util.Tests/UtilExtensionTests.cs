using FalkInstaller.Extensibility;
using FalkInstaller.Extensions.Util.XmlConfig;
using Xunit;

namespace FalkInstaller.Extensions.Util.Tests;

public sealed class UtilExtensionTests
{
    [Fact]
    public void Name_ReturnsUtil()
    {
        var extension = new UtilExtension();

        Assert.Equal("Util", extension.Name);
    }

    [Fact]
    public void Extension_ImplementsIFalkInstallerExtension()
    {
        var extension = new UtilExtension();

        Assert.IsAssignableFrom<IFalkInstallerExtension>(extension);
    }

    [Fact]
    public void Register_RegistersXmlConfigTableContributor()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Single(registry.TableContributors);
        Assert.IsType<XmlConfigTableContributor>(registry.TableContributors[0]);
    }

    [Fact]
    public void Register_XmlConfigContributor_HasCorrectTableName()
    {
        var extension = new UtilExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Equal("XmlConfig", registry.TableContributors[0].TableName);
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
        public List<IExtensionValidator> Validators { get; } = [];

        public void RegisterTableContributor(IMsiTableContributor contributor) =>
            TableContributors.Add(contributor);

        public void RegisterComponentContributor(IComponentContributor contributor) =>
            ComponentContributors.Add(contributor);

        public void RegisterValidator(IExtensionValidator validator) =>
            Validators.Add(validator);
    }
}
