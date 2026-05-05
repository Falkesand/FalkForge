using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class MoveFileTableProducerTests
{
    [Fact]
    public void Schema_has_seven_columns_filekey_pk_one_fk()
    {
        MoveFileTableProducer producer = new();

        Assert.Equal("MoveFile", producer.Schema.Name.Value);
        Assert.Equal(7, producer.Schema.Columns.Length);
        Assert.Equal("FileKey", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal("SourceName", producer.Schema.Columns[2].Name);
        Assert.Equal("SourceFolder", producer.Schema.Columns[3].Name);
        Assert.Equal("DestName", producer.Schema.Columns[4].Name);
        Assert.Equal("DestFolder", producer.Schema.Columns[5].Name);
        Assert.Equal("Options", producer.Schema.Columns[6].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // Only Component_ is a compile-time FK. SourceFolder and DestFolder are
        // MSI property/directory references resolved at install time — not strict
        // compile-time FKs. Matches legacy TableEmitter's SetString behaviour.
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
