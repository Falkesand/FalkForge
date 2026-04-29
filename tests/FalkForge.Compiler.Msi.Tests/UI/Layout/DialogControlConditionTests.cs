using System;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogControlConditionTests
{
    [Fact]
    public void Construct_with_required_fields_succeeds()
    {
        var condition = new DialogControlCondition
        {
            Control = "InstallButton",
            Action = "Disable",
            Condition = "NOT INSTALLDIR",
        };

        Assert.Equal("InstallButton", condition.Control);
        Assert.Equal("Disable", condition.Action);
        Assert.Equal("NOT INSTALLDIR", condition.Condition);
    }

    [Fact]
    public void Construct_with_empty_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = string.Empty,
            Action = "Disable",
            Condition = "1",
        });
    }

    [Fact]
    public void Construct_with_whitespace_control_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = "  ",
            Action = "Disable",
            Condition = "1",
        });
    }

    [Fact]
    public void Construct_with_empty_action_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = "InstallButton",
            Action = string.Empty,
            Condition = "1",
        });
    }

    [Fact]
    public void Construct_with_whitespace_action_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = "InstallButton",
            Action = "  ",
            Condition = "1",
        });
    }

    [Fact]
    public void Construct_with_empty_condition_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = "InstallButton",
            Action = "Disable",
            Condition = string.Empty,
        });
    }

    [Fact]
    public void Construct_with_whitespace_condition_throws()
    {
        Assert.Throws<ArgumentException>(() => new DialogControlCondition
        {
            Control = "InstallButton",
            Action = "Disable",
            Condition = "  ",
        });
    }
}
