using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class RecipeColumnTests
{
    [Fact]
    public void Construct_with_valid_inputs_succeeds()
    {
        RecipeColumn column = new()
        {
            Name = "Property",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        };

        Assert.Equal("Property", column.Name);
        Assert.Equal(ColumnType.String, column.Type);
        Assert.Equal(72, column.Width);
        Assert.False(column.Nullable);
        Assert.False(column.LocalizableKey);
    }

    [Fact]
    public void Construct_with_nullable_and_localizable_succeeds()
    {
        RecipeColumn column = new()
        {
            Name = "Description",
            Type = ColumnType.Localized,
            Width = 255,
            Nullable = true,
            LocalizableKey = true
        };

        Assert.True(column.Nullable);
        Assert.True(column.LocalizableKey);
    }

    [Fact]
    public void Construct_with_null_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new RecipeColumn
        {
            Name = null!,
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        });
    }

    [Fact]
    public void Construct_with_empty_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new RecipeColumn
        {
            Name = "",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        });
    }

    [Fact]
    public void Construct_with_name_starting_with_digit_throws()
    {
        Assert.Throws<ArgumentException>(() => new RecipeColumn
        {
            Name = "1Column",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        });
    }

    [Fact]
    public void Construct_with_sql_injection_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new RecipeColumn
        {
            Name = "Name`; DROP TABLE Property;--",
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        });
    }

    [Fact]
    public void Construct_with_too_long_name_throws()
    {
        Assert.Throws<ArgumentException>(() => new RecipeColumn
        {
            Name = new string('A', 32),
            Type = ColumnType.String,
            Width = 72,
            Nullable = false,
            LocalizableKey = false
        });
    }

    [Fact]
    public void Equal_columns_compare_equal()
    {
        RecipeColumn a = new() { Name = "X", Type = ColumnType.Integer, Width = 4, Nullable = false, LocalizableKey = false };
        RecipeColumn b = new() { Name = "X", Type = ColumnType.Integer, Width = 4, Nullable = false, LocalizableKey = false };

        Assert.Equal(a, b);
    }
}
