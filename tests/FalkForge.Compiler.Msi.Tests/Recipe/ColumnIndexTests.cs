using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class ColumnIndexTests
{
    [Fact]
    public void Default_value_is_zero()
    {
        ColumnIndex idx = default;

        Assert.Equal(0, idx.Value);
    }

    [Fact]
    public void Construct_with_zero_succeeds()
    {
        ColumnIndex idx = new(0);

        Assert.Equal(0, idx.Value);
    }

    [Fact]
    public void Construct_with_positive_succeeds()
    {
        ColumnIndex idx = new(7);

        Assert.Equal(7, idx.Value);
    }

    [Fact]
    public void Construct_with_negative_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ColumnIndex(-1));
    }

    [Fact]
    public void Equal_values_are_equal()
    {
        ColumnIndex a = new(3);
        ColumnIndex b = new(3);

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Different_values_are_not_equal()
    {
        ColumnIndex a = new(1);
        ColumnIndex b = new(2);

        Assert.NotEqual(a, b);
    }
}
