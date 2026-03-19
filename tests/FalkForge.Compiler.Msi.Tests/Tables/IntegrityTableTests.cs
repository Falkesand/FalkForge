using FalkForge.Compiler.Msi.Tables;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Tables;

public sealed class IntegrityTableTests
{
    [Fact]
    public void CreateFalkForgeIntegrityTable_ContainsCreateTableStatement()
    {
        Assert.Contains("CREATE TABLE", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_UsesUnderscorePrefix()
    {
        // Underscore-prefixed tables are reserved for custom/vendor tables in MSI
        Assert.Contains("`_FalkForgeIntegrity`", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_HasIdColumn()
    {
        Assert.Contains("`Id` CHAR(72) NOT NULL", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_HasFormatColumn()
    {
        Assert.Contains("`Format` CHAR(64) NOT NULL", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_HasDataColumn()
    {
        Assert.Contains("`Data` LONGCHAR NOT NULL", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_HasPrimaryKeyOnId()
    {
        Assert.Contains("PRIMARY KEY `Id`", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }

    [Fact]
    public void CreateFalkForgeIntegrityTable_IsSingleLine()
    {
        // MSI SQL engine does not tolerate multi-line strings
        Assert.DoesNotContain("\n", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
        Assert.DoesNotContain("\r", MsiTableDefinitions.CreateFalkForgeIntegrityTable);
    }
}
