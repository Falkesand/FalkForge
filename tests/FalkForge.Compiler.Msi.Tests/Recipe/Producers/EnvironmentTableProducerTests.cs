using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class EnvironmentTableProducerTests
{
    [Fact]
    public void Schema_has_four_columns_environment_pk_component_fk()
    {
        EnvironmentTableProducer producer = new();

        Assert.Equal("Environment", producer.Schema.Name.Value);
        Assert.Equal(4, producer.Schema.Columns.Length);
        Assert.Equal("Environment", producer.Schema.Columns[0].Name);
        Assert.Equal("Name", producer.Schema.Columns[1].Name);
        Assert.Equal("Value", producer.Schema.Columns[2].Name);
        Assert.Equal("Component_", producer.Schema.Columns[3].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(3, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
