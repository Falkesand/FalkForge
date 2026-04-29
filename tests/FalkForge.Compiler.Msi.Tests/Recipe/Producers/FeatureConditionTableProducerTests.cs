using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class FeatureConditionTableProducerTests
{
    [Fact]
    public void Schema_has_three_columns_composite_pk_feature_fk()
    {
        FeatureConditionTableProducer producer = new();

        Assert.Equal("Condition", producer.Schema.Name.Value);
        Assert.Equal(3, producer.Schema.Columns.Length);
        Assert.Equal("Feature_", producer.Schema.Columns[0].Name);
        Assert.Equal("Level", producer.Schema.Columns[1].Name);
        Assert.Equal("Condition", producer.Schema.Columns[2].Name);
        Assert.Equal(2, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(0, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Feature", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
