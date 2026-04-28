using FalkForge.Compiler.Msi.Recipe;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

public sealed class TableIdTests
{
    [Fact]
    public void Create_with_simple_identifier_succeeds()
    {
        Result<TableId> result = TableId.Create("Property");

        Assert.True(result.IsSuccess);
        Assert.Equal("Property", result.Value.Value);
    }

    [Fact]
    public void Create_with_underscore_prefix_succeeds()
    {
        Result<TableId> result = TableId.Create("_Tables");

        Assert.True(result.IsSuccess);
        Assert.Equal("_Tables", result.Value.Value);
    }

    [Fact]
    public void Create_with_digits_after_first_char_succeeds()
    {
        Result<TableId> result = TableId.Create("Component2");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Create_with_max_length_31_succeeds()
    {
        string name = new('A', 31);

        Result<TableId> result = TableId.Create(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.Value);
    }

    [Fact]
    public void Create_with_empty_string_fails()
    {
        Result<TableId> result = TableId.Create("");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_null_fails()
    {
        Result<TableId> result = TableId.Create(null!);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_starting_with_digit_fails()
    {
        Result<TableId> result = TableId.Create("1Property");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_sql_injection_attempt_fails()
    {
        Result<TableId> result = TableId.Create("Property; DROP TABLE Component;");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_space_fails()
    {
        Result<TableId> result = TableId.Create("My Table");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_dash_fails()
    {
        Result<TableId> result = TableId.Create("My-Table");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_length_32_fails()
    {
        string name = new('A', 32);

        Result<TableId> result = TableId.Create(name);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Create_with_unicode_fails()
    {
        Result<TableId> result = TableId.Create("Tabellä");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Equal_values_have_equal_records()
    {
        TableId a = TableId.Create("Property").Value;
        TableId b = TableId.Create("Property").Value;

        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Different_values_are_not_equal()
    {
        TableId a = TableId.Create("Property").Value;
        TableId b = TableId.Create("Component").Value;

        Assert.NotEqual(a, b);
    }
}
