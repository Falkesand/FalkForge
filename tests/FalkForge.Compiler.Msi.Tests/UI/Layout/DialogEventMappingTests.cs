using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogEventMappingTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var mapping = new DialogEventMapping
        {
            Control = "ProgressBar",
            Event = "SetProgress",
            Attribute = "Progress",
        };

        Assert.Equal("ProgressBar", mapping.Control);
        Assert.Equal("SetProgress", mapping.Event);
        Assert.Equal("Progress", mapping.Attribute);
    }

    [Fact]
    public void Construct_with_empty_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = string.Empty,
            Event = "SetProgress",
            Attribute = "Progress",
        });
    }

    [Fact]
    public void Construct_with_whitespace_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = "  ",
            Event = "SetProgress",
            Attribute = "Progress",
        });
    }

    [Fact]
    public void Construct_with_empty_event_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = "ProgressBar",
            Event = string.Empty,
            Attribute = "Progress",
        });
    }

    [Fact]
    public void Construct_with_whitespace_event_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = "ProgressBar",
            Event = "  ",
            Attribute = "Progress",
        });
    }

    [Fact]
    public void Construct_with_empty_attribute_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = "ProgressBar",
            Event = "SetProgress",
            Attribute = string.Empty,
        });
    }

    [Fact]
    public void Construct_with_whitespace_attribute_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogEventMapping
        {
            Control = "ProgressBar",
            Event = "SetProgress",
            Attribute = "  ",
        });
    }
}
