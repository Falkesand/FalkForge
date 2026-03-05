using System.Diagnostics;

namespace FalkForge.Engine;

internal sealed class DefaultUpdateLauncher : IUpdateLauncher
{
    private readonly string? _cacheRoot;

    internal DefaultUpdateLauncher(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot;
    }

    public Result<Unit> Launch(string updatePath)
    {
        // Path containment check
        if (_cacheRoot is not null)
        {
            var fullUpdate = Path.GetFullPath(updatePath);
            var fullCache = Path.GetFullPath(_cacheRoot);
            // Ensure cache path ends with separator to prevent prefix attacks
            // (e.g. /tmp/cache matching /tmp/cachemalicious)
            if (!fullCache.EndsWith(Path.DirectorySeparatorChar))
                fullCache += Path.DirectorySeparatorChar;
            if (!fullUpdate.StartsWith(fullCache, StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(new Error(ErrorKind.SecurityError,
                    "UPD005: Update path is outside the cache root."));
        }

        if (!File.Exists(updatePath))
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Update file not found: '{Path.GetFileName(updatePath)}'."));

        try
        {
            Process.Start(new ProcessStartInfo(updatePath) { UseShellExecute = true });
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(new Error(ErrorKind.EngineError,
                $"UPD005: Failed to launch update: {ex.Message}"));
        }
    }
}
