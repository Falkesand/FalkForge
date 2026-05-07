namespace FalkForge.Engine.Pipeline;

using System.Text.Json;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;

/// <summary>
/// Production <see cref="ILayoutStore"/> that serializes the installer manifest as
/// <c>manifest.json</c> inside the layout directory using the AOT-safe
/// <see cref="LayoutJsonContext"/>.
/// </summary>
public sealed class FileSystemLayoutStore : ILayoutStore
{
    private const string ManifestFileName = "manifest.json";

    /// <inheritdoc/>
    public async Task<Result<Unit>> WriteAsync(InstallerManifest manifest, string layoutPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(layoutPath))
            return Result<Unit>.Failure(ErrorKind.LayoutError, "Layout path cannot be empty.");

        try
        {
            Directory.CreateDirectory(layoutPath);
            var manifestPath = Path.Combine(layoutPath, ManifestFileName);
            var json = JsonSerializer.SerializeToUtf8Bytes(manifest, LayoutJsonContext.Default.InstallerManifest);
            await File.WriteAllBytesAsync(manifestPath, json, ct);
            return Unit.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Failure(ErrorKind.LayoutError,
                $"Failed to write layout manifest: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<Result<InstallerManifest>> ReadAsync(string layoutPath, CancellationToken ct)
    {
        var manifestPath = Path.Combine(layoutPath, ManifestFileName);

        if (!File.Exists(manifestPath))
            return Result<InstallerManifest>.Failure(ErrorKind.FileNotFound,
                $"No manifest.json found at '{layoutPath}'.");

        try
        {
            var json = await File.ReadAllBytesAsync(manifestPath, ct);
            var manifest = JsonSerializer.Deserialize(json, LayoutJsonContext.Default.InstallerManifest);

            return manifest is not null
                ? Result<InstallerManifest>.Success(manifest)
                : Result<InstallerManifest>.Failure(ErrorKind.LayoutError,
                    $"manifest.json at '{layoutPath}' deserialized to null.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Result<InstallerManifest>.Failure(ErrorKind.LayoutError,
                $"Failed to read layout manifest: {ex.Message}");
        }
    }
}
