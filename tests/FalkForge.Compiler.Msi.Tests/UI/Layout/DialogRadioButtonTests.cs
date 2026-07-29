using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogRadioButtonTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var radioButton = new DialogRadioButton
        {
            Property = "FalkForgeRMOption",
            Value = "UseRM",
        };

        Assert.Equal("FalkForgeRMOption", radioButton.Property);
        Assert.Equal("UseRM", radioButton.Value);
    }

    [Fact]
    public void Construct_with_empty_property_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogRadioButton
        {
            Property = string.Empty,
            Value = "UseRM",
        });
    }

    [Fact]
    public void Construct_with_whitespace_property_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogRadioButton
        {
            Property = "   ",
            Value = "UseRM",
        });
    }
}
