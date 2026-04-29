using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class UpgradeTableProducerTests
{
    [Fact]
    public void Schema_has_seven_columns_composite_pk_no_fks()
    {
        UpgradeTableProducer producer = new();

        Assert.Equal("Upgrade", producer.Schema.Name.Value);
        Assert.Equal(7, producer.Schema.Columns.Length);
        Assert.Equal("UpgradeCode", producer.Schema.Columns[0].Name);
        Assert.Equal("VersionMin", producer.Schema.Columns[1].Name);
        Assert.Equal("VersionMax", producer.Schema.Columns[2].Name);
        Assert.Equal("Language", producer.Schema.Columns[3].Name);
        Assert.Equal("Attributes", producer.Schema.Columns[4].Name);
        Assert.Equal("Remove", producer.Schema.Columns[5].Name);
        Assert.Equal("ActionProperty", producer.Schema.Columns[6].Name);

        // Composite PK matches MsiTableDefinitions.CreateUpgradeTable: UpgradeCode, VersionMin, VersionMax, Language, Attributes
        Assert.Equal(5, producer.Schema.PrimaryKey.Length);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(1, producer.Schema.PrimaryKey[1].Value);
        Assert.Equal(2, producer.Schema.PrimaryKey[2].Value);
        Assert.Equal(3, producer.Schema.PrimaryKey[3].Value);
        Assert.Equal(4, producer.Schema.PrimaryKey[4].Value);

        Assert.Empty(producer.Schema.ForeignKeys);
    }
}
