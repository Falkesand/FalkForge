using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ShortcutTableProducerTests
{
    [Fact]
    public void Schema_has_shortcut_pk_directory_and_component_fks()
    {
        ShortcutTableProducer producer = new();

        Assert.Equal("Shortcut", producer.Schema.Name.Value);
        Assert.True(producer.Schema.Columns.Length >= 12);
        Assert.Equal("Shortcut", producer.Schema.Columns[0].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Equal(2, producer.Schema.ForeignKeys.Length);
        Assert.Equal("Directory", producer.Schema.ForeignKeys[0].TargetTable.Value);
        Assert.Equal("Component", producer.Schema.ForeignKeys[1].TargetTable.Value);
    }
}
