namespace FalkForge.Engine.Elevation;

/// <summary>
/// Engine-side abstraction for sending commands to the elevated companion process.
/// </summary>
public interface IElevationClient : IAsyncDisposable
{
    Task<Result<byte[]>> SendCommandAsync(string commandName, byte[] payload, CancellationToken cancellationToken = default, IProgress<int>? progress = null);
}
