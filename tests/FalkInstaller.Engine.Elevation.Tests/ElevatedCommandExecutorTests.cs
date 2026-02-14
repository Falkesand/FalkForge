using FalkInstaller.Engine.Protocol.Messages;
using Xunit;

namespace FalkInstaller.Engine.Elevation.Tests;

public sealed class ElevatedCommandExecutorTests
{
    [Fact]
    public void Execute_KnownCommand_DispatchesToCorrectCommand()
    {
        var mock = new MockCommand { ResponsePayload = new byte[] { 1, 2, 3 } };
        var executor = new ElevatedCommandExecutor(new[] { mock });
        var message = new ElevateExecuteMessage
        {
            SequenceId = 42,
            CommandName = "Mock",
            CommandPayload = new byte[] { 10, 20 }
        };

        var result = executor.Execute(message);

        Assert.True(result.Success);
        Assert.Equal(42u, result.SequenceId);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.ResultPayload);
        Assert.Equal(new byte[] { 10, 20 }, mock.LastPayload);
    }

    [Fact]
    public void Execute_UnknownCommand_ReturnsFailure()
    {
        var executor = new ElevatedCommandExecutor(Array.Empty<MockCommand>());
        var message = new ElevateExecuteMessage
        {
            SequenceId = 7,
            CommandName = "NonExistent",
            CommandPayload = Array.Empty<byte>()
        };

        var result = executor.Execute(message);

        Assert.False(result.Success);
        Assert.Equal(7u, result.SequenceId);
        Assert.Contains("Unknown command: NonExistent", result.ErrorMessage);
    }

    [Fact]
    public void Execute_CommandFailure_PropagatesErrorMessage()
    {
        var mock = new MockCommand { ShouldFail = true, FailureMessage = "Something broke" };
        var executor = new ElevatedCommandExecutor(new[] { mock });
        var message = new ElevateExecuteMessage
        {
            SequenceId = 99,
            CommandName = "Mock",
            CommandPayload = Array.Empty<byte>()
        };

        var result = executor.Execute(message);

        Assert.False(result.Success);
        Assert.Equal(99u, result.SequenceId);
        Assert.Equal("Something broke", result.ErrorMessage);
    }

    [Fact]
    public void Execute_SequenceId_PreservedFromRequestToResponse()
    {
        var mock = new MockCommand();
        var executor = new ElevatedCommandExecutor(new[] { mock });

        var sequenceIds = new uint[] { 0, 1, 100, uint.MaxValue };
        foreach (var id in sequenceIds)
        {
            var message = new ElevateExecuteMessage
            {
                SequenceId = id,
                CommandName = "Mock",
                CommandPayload = Array.Empty<byte>()
            };

            var result = executor.Execute(message);
            Assert.Equal(id, result.SequenceId);
        }
    }

    [Fact]
    public void Execute_EmptyPayloadResult_SetsResultPayloadToNull()
    {
        var mock = new MockCommand { ResponsePayload = Array.Empty<byte>() };
        var executor = new ElevatedCommandExecutor(new[] { mock });
        var message = new ElevateExecuteMessage
        {
            SequenceId = 1,
            CommandName = "Mock",
            CommandPayload = Array.Empty<byte>()
        };

        var result = executor.Execute(message);

        Assert.True(result.Success);
        Assert.Null(result.ResultPayload);
    }
}
