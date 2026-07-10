namespace FalkForge.Engine.Elevation.Commands;

public sealed class FileWriteCommand : IElevatedCommand
{
    public string Name => "FileWrite";

    public Result<byte[]> Execute(byte[] payload, Action<int>? onProgress = null)
    {
        using var stream = new MemoryStream(payload);
        using var reader = new BinaryReader(stream);

        var targetPath = reader.ReadString();
        var contentLength = reader.ReadInt32();
        var content = reader.ReadBytes(contentLength);

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(targetPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Result<byte[]>.Failure(ErrorKind.SecurityError, $"Invalid path: {ex.Message}");
        }

        if (normalizedPath.Contains("..", StringComparison.Ordinal))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Path must not contain '..' segments after normalization");

        if (!IsAllowedPath(normalizedPath))
            return Result<byte[]>.Failure(ErrorKind.SecurityError, "Path is outside allowed directories (Program Files, ProgramData, or user profile)");

        try
        {
            var dir = Path.GetDirectoryName(normalizedPath);
            if (dir is not null)
            {
                // Reject if ANY ancestor (up to the allowed root) is a junction/symlink, and
                // create each missing level ourselves so no write walks through a reparse point.
                var treeResult = ElevatedPathPolicy.EnsureDirectoryTreeSafe(dir, ElevatedPathPolicy.FileWriteRoots());
                if (treeResult.IsFailure)
                    return Result<byte[]>.Failure(treeResult.Error);
            }

            // Reject an existing target that is itself a reparse point (a file symlink would
            // otherwise redirect the elevated write to the link's target).
            if (File.Exists(normalizedPath) &&
                File.GetAttributes(normalizedPath).HasFlag(FileAttributes.ReparsePoint))
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "Target file is a symbolic link and cannot be written to");

            File.WriteAllBytes(normalizedPath, content);
            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<byte[]>.Failure(ErrorKind.ElevationError, $"File write failed: {ex.Message}");
        }
    }

    internal static bool IsAllowedPath(string normalizedPath) =>
        ElevatedPathPolicy.IsUnderAllowedRoot(normalizedPath, ElevatedPathPolicy.FileWriteRoots());
}
