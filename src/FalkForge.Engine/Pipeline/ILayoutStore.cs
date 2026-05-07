namespace FalkForge.Engine.Pipeline;

using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Manifest persistence port for installer layout mode. Hides JSON serialization,
/// file I/O, and AOT context details from phase-step code.
/// </summary>
public interface ILayoutStore
{
    /// <summary>
    /// Serializes <paramref name="manifest"/> as the layout manifest file inside
    /// <paramref name="layoutPath"/>.
    /// </summary>
    Task<Result<Unit>> WriteAsync(InstallerManifest manifest, string layoutPath, CancellationToken ct);

    /// <summary>
    /// Reads and deserializes the layout manifest from <paramref name="layoutPath"/>.
    /// Returns <see cref="ErrorKind.FileNotFound"/> when the file is absent.
    /// </summary>
    Task<Result<InstallerManifest>> ReadAsync(string layoutPath, CancellationToken ct);
}
