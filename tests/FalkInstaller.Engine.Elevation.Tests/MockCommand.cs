using FalkInstaller.Engine.Elevation.Commands;

namespace FalkInstaller.Engine.Elevation.Tests;

public sealed class MockCommand : IElevatedCommand
{
    public string Name { get; init; } = "Mock";
    public byte[] ResponsePayload { get; set; } = Array.Empty<byte>();
    public bool ShouldFail { get; set; }
    public string FailureMessage { get; set; } = "Mock failure";
    public byte[]? LastPayload { get; private set; }

    public Result<byte[]> Execute(byte[] payload)
    {
        LastPayload = payload;
        return ShouldFail
            ? Result<byte[]>.Failure(ErrorKind.ExecutionError, FailureMessage)
            : ResponsePayload;
    }
}
