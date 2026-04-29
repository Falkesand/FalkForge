using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

public sealed class ServiceInstallTableProducerTests
{
    [Fact]
    public void Schema_has_serviceinstall_pk_and_component_fk()
    {
        ServiceInstallTableProducer producer = new();

        Assert.Equal("ServiceInstall", producer.Schema.Name.Value);
        Assert.True(producer.Schema.Columns.Length >= 12);
        Assert.Equal("ServiceInstall", producer.Schema.Columns[0].Name);
        Assert.Single(producer.Schema.PrimaryKey);
        Assert.Equal(0, producer.Schema.PrimaryKey[0].Value);
        Assert.Single(producer.Schema.ForeignKeys);
        Assert.Equal("Component", producer.Schema.ForeignKeys[0].TargetTable.Value);
    }
}
