using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class RemoveFileTableProducerTests
{
    [Fact]
    public void Schema_has_five_columns_filekey_pk_component_fk_only()
    {
        RemoveFileTableProducer producer = new();

        Assert.Equal("RemoveFile", producer.Schema.Name.Value);
        Assert.Equal(5, producer.Schema.Columns.Length);
        Assert.Equal("FileKey", producer.Schema.Columns[0].Name);
        Assert.Equal("Component_", producer.Schema.Columns[1].Name);
        Assert.Equal("FileName", producer.Schema.Columns[2].Name);
        Assert.Equal("DirProperty", producer.Schema.Columns[3].Name);
        Assert.Equal("InstallMode", producer.Schema.Columns[4].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);

        // DirProperty (col 3) accepts Directory keys or property names resolved at
        // install time — not a compile-time FK. Only Component_ (col 1) is strict.
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal(1, producer.Schema.ForeignKeys[0].SourceColumn.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
