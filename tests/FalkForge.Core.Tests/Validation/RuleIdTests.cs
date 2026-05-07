using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class RuleIdTests
{
    [Fact]
    public void Value_round_trips_through_implicit_string_conversion()
    {
        var id = new RuleId("PKG001");

        string asString = id;

        Assert.Equal("PKG001", asString);
    }

    [Theory]
    [InlineData("PKG001", "PKG")]
    [InlineData("SVC005", "SVC")]
    [InlineData("FEA002", "FEA")]
    [InlineData("CTB011", "CTB")]
    [InlineData("REG007", "REG")]
    public void Prefix_extracts_alphabetic_prefix(string value, string expectedPrefix)
    {
        var id = new RuleId(value);

        Assert.Equal(expectedPrefix, id.Prefix);
    }

    [Fact]
    public void Equality_is_value_based()
    {
        var a = new RuleId("PKG001");
        var b = new RuleId("PKG001");
        var c = new RuleId("PKG002");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ToString_returns_the_value()
    {
        var id = new RuleId("SVC003");

        Assert.Equal("SVC003", id.ToString());
    }
}
