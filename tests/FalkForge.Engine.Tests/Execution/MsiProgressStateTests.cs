namespace FalkForge.Engine.Tests.Execution;

using FalkForge.Engine.Execution;
using Xunit;

public class MsiProgressStateTests
{
    private const uint ProgressFlag = 0x0400;
    private const uint OtherFlag = 0x0001;

    [Fact]
    public void ProcessMessage_NonProgressMessage_ReturnsNegativeOne()
    {
        var state = new MsiProgressState();
        var result = state.ProcessMessage(OtherFlag, "1: 0 2: 100");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ProcessMessage_MasterReset_ReturnsZero()
    {
        var state = new MsiProgressState();
        var result = state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 0 4: 1");
        Assert.Equal(0, result);
    }

    [Fact]
    public void ProcessMessage_TickAfterReset_ReportsPercent()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 0 4: 1");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        Assert.Equal(50, result);
    }

    [Fact]
    public void ProcessMessage_MultipleTicks_Accumulates()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 200 3: 0 4: 1");
        state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        Assert.Equal(50, result);
    }

    [Fact]
    public void ProcessMessage_ExceedsTotal_ClampedTo100()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 0 4: 1");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 150");
        Assert.Equal(100, result);
    }

    [Fact]
    public void ProcessMessage_TickBeforeReset_ReturnsNegativeOne()
    {
        var state = new MsiProgressState();
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ProcessMessage_ActionInfo_ReturnsNegativeOne()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 0 4: 1");
        var result = state.ProcessMessage(ProgressFlag, "1: 1 2: 1 3: 0");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ProcessMessage_ReverseDirection_DecrementsFromTotal()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 1 4: 1");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 30");
        Assert.Equal(30, result);
    }

    [Fact]
    public void ProcessMessage_EmptyMessage_ReturnsNegativeOne()
    {
        var state = new MsiProgressState();
        var result = state.ProcessMessage(ProgressFlag, "");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ProcessMessage_MalformedMessage_ReturnsNegativeOne()
    {
        var state = new MsiProgressState();
        var result = state.ProcessMessage(ProgressFlag, "not a valid message");
        Assert.Equal(-1, result);
    }

    [Fact]
    public void ProcessMessage_SecondReset_ResetsProgress()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 0 4: 1");
        state.ProcessMessage(ProgressFlag, "1: 2 2: 80");
        state.ProcessMessage(ProgressFlag, "1: 0 2: 200 3: 0 4: 1");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        Assert.Equal(25, result);
    }

    [Fact]
    public void ProcessMessage_MasterReset_MissingField3_DefaultsToForward()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        Assert.Equal(50, result);
    }

    [Fact]
    public void ProcessMessage_ReverseDirection_ExceedsBounds_ClampedTo100()
    {
        var state = new MsiProgressState();
        state.ProcessMessage(ProgressFlag, "1: 0 2: 100 3: 1 4: 1");
        state.ProcessMessage(ProgressFlag, "1: 2 2: 50");
        var result = state.ProcessMessage(ProgressFlag, "1: 2 2: 100");
        Assert.Equal(100, result);
    }
}
