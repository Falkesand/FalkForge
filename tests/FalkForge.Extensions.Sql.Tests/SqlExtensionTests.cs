using FalkForge.Extensibility;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests;

public sealed class SqlExtensionTests
{
    [Fact]
    public void Name_ReturnsSql()
    {
        var extension = new SqlExtension();

        Assert.Equal("Sql", extension.Name);
    }

    [Fact]
    public void Extension_ImplementsIFalkForgeExtension()
    {
        var extension = new SqlExtension();

        Assert.IsAssignableFrom<IFalkForgeExtension>(extension);
    }

    [Fact]
    public void Register_RegistersThreeTableContributors()
    {
        var extension = new SqlExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.Equal(3, registry.TableContributors.Count);
    }

    [Fact]
    public void Register_RegistersDatabaseContributor()
    {
        var extension = new SqlExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.IsType<SqlDatabaseTableContributor>(registry.TableContributors[0]);
        Assert.Equal("SqlDatabase", registry.TableContributors[0].TableName);
    }

    [Fact]
    public void Register_RegistersScriptContributor()
    {
        var extension = new SqlExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.IsType<SqlScriptTableContributor>(registry.TableContributors[1]);
        Assert.Equal("SqlScript", registry.TableContributors[1].TableName);
    }

    [Fact]
    public void Register_RegistersStringContributor()
    {
        var extension = new SqlExtension();
        var registry = new TestExtensionRegistry();

        extension.Register(registry);

        Assert.IsType<SqlStringTableContributor>(registry.TableContributors[2]);
        Assert.Equal("SqlString", registry.TableContributors[2].TableName);
    }

    [Fact]
    public void Databases_ReturnsSameInstanceAcrossCalls()
    {
        var extension = new SqlExtension();

        Assert.Same(extension.Databases, extension.Databases);
    }

    [Fact]
    public void Scripts_ReturnsSameInstanceAcrossCalls()
    {
        var extension = new SqlExtension();

        Assert.Same(extension.Scripts, extension.Scripts);
    }

    [Fact]
    public void Strings_ReturnsSameInstanceAcrossCalls()
    {
        var extension = new SqlExtension();

        Assert.Same(extension.Strings, extension.Strings);
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
