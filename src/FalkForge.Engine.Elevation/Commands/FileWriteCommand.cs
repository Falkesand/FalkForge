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
            if (dir is null)
                return Result<byte[]>.Failure(ErrorKind.SecurityError,
                    "Target path must include a file name under an allowed directory");

            // Outer gate: reject if ANY ancestor (up to the allowed root) is a junction/symlink,
            // and create each missing level ourselves. This defeats a junction planted BEFORE
            // the check; the handle-based write below is the inner enforcement against a swap
            // planted AFTER it.
            var treeResult = ElevatedPathPolicy.EnsureDirectoryTreeSafe(dir, ElevatedPathPolicy.FileWriteRoots());
            if (treeResult.IsFailure)
                return Result<byte[]>.Failure(treeResult.Error);

            // Inner enforcement: pin the parent directory by a verified no-follow handle, open
            // the target no-follow, verify BOTH handles (reparse attribute + true final path),
            // and write only through the verified handle. A leaf symlink — dangling or not —
            // and a post-check junction swap are both rejected here. See NoFollowFileWriter
            // for the mechanism and the precise (narrow) residual that remains.
            var writeResult = NoFollowFileWriter.Write(dir, normalizedPath, content);
            if (writeResult.IsFailure)
                return Result<byte[]>.Failure(writeResult.Error);

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
