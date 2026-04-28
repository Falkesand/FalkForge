using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class ColumnTypeTests
{
    [Fact]
    public void Has_four_distinct_values()
    {
        Array values = Enum.GetValues(typeof(ColumnType));

        Assert.Equal(4, values.Length);
    }

    [Theory]
    [InlineData(ColumnType.Integer)]
    [InlineData(ColumnType.String)]
    [InlineData(ColumnType.Localized)]
    [InlineData(ColumnType.Binary)]
    public void Defined_member_round_trips(ColumnType value)
    {
        Assert.True(Enum.IsDefined(typeof(ColumnType), value));
    }
}
