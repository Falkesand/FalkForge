using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class RegistryTableProducerTests
{
    [Fact]
    public void Schema_has_six_columns_registry_pk_component_fk()
    {
        RegistryTableProducer producer = new();

        Assert.Equal("Registry", producer.Schema.Name.Value);
        Assert.Equal(6, producer.Schema.Columns.Length);
        Assert.Equal("Registry", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[5].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(5, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
