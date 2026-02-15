namespace FalkForge.Engine.Elevation.Commands;

public interface IElevatedCommand
{
    string Name { get; }
    Result<byte[]> Execute(byte[] payload);
}
