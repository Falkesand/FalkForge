using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogControlEventTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var evt = new DialogControlEvent
        {
            Control = "NextButton",
            Event = "NewDialog",
        };

        Assert.Equal("NextButton", evt.Control);
        Assert.Equal("NewDialog", evt.Event);
        Assert.Equal(string.Empty, evt.Argument);
        Assert.Null(evt.Condition);
        Assert.Equal(1, evt.Order);
    }

    [Fact]
    public void Construct_with_all_fields_succeeds()
    {
        var evt = new DialogControlEvent
        {
            Control = "BrowseButton",
            Event = "SpawnDialog",
            Argument = "BrowseDlg",
            Condition = "1",
            Order = 3,
        };

        Assert.Equal("BrowseButton", evt.Control);
        Assert.Equal("SpawnDialog", evt.Event);
        Assert.Equal("BrowseDlg", evt.Argument);
        Assert.Equal("1", evt.Condition);
        Assert.Equal(3, evt.Order);
    }

    [Fact]
    public void Construct_with_empty_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlEvent
        {
            Control = string.Empty,
            Event = "NewDialog",
        });
    }

    [Fact]
    public void Construct_with_whitespace_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlEvent
        {
            Control = "  ",
            Event = "NewDialog",
        });
    }

    [Fact]
    public void Construct_with_empty_event_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlEvent
        {
            Control = "NextButton",
            Event = string.Empty,
        });
    }

    [Fact]
    public void Construct_with_whitespace_event_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlEvent
        {
            Control = "NextButton",
            Event = "  ",
        });
    }

    [Fact]
    public void Default_argument_is_empty_string()
    {
        var evt = new DialogControlEvent
        {
            Control = "NextButton",
            Event = "EndDialog",
        };

        Assert.Equal(string.Empty, evt.Argument);
    }

    [Fact]
    public void Default_order_is_one()
    {
        var evt = new DialogControlEvent
        {
            Control = "NextButton",
            Event = "EndDialog",
        };

        Assert.Equal(1, evt.Order);
    }

    [Fact]
    public void Default_condition_is_null()
    {
        var evt = new DialogControlEvent
        {
            Control = "NextButton",
            Event = "EndDialog",
        };

        Assert.Null(evt.Condition);
    }
}
