using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class RecipeTableTests
{
    [Fact]
    public void Construct_with_all_required_members_succeeds()
    {
        TableId name = TableId.Create("Property").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn { Name = "Property", Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Value", Type = ColumnType.String, Width = 0, Nullable = true, LocalizableKey = false });
        ImmutableArray<RecipeRow> rows = ImmutableArray.Create(
            new RecipeRow { Cells = ImmutableArray.Create<CellValue>(new CellValue.StringValue("ProductCode"), new CellValue.StringValue("{ABC}")) });
        ImmutableArray<ColumnIndex> pk = ImmutableArray.Create(new ColumnIndex(0));

        RecipeTable table = new()
        {
            Name = name,
            Columns = columns,
            Rows = rows,
            PrimaryKey = pk,
            CreateTableSql = "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR PRIMARY KEY `Property`)",
            InsertViewSql = "SELECT `Property`, `Value` FROM `Property`"
        };

        Assert.Equal(name, table.Name);
        Assert.Equal(2, table.Columns.Length);
        Assert.Single(table.Rows);
        Assert.Equal(pk, table.PrimaryKey);
        Assert.Contains("CREATE TABLE", table.CreateTableSql, StringComparison.Ordinal);
        Assert.Contains("SELECT", table.InsertViewSql, StringComparison.Ordinal);
    }
}
