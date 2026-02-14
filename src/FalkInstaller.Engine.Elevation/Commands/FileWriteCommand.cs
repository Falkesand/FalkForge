namespace FalkInstaller.Engine.Elevation.Commands;

public sealed class FileWriteCommand : IElevatedCommand
{
    public string Name => "FileWrite";

    public Result<byte[]> Execute(byte[] payload)
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
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(normalizedPath, content);
            return Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Result<byte[]>.Failure(ErrorKind.ElevationError, $"File write failed: {ex.Message}");
        }
    }

    internal static bool IsAllowedPath(string normalizedPath)
    {
        ReadOnlySpan<string> allowedPrefixes =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        ];

        foreach (var prefix in allowedPrefixes)
        {
            if (!string.IsNullOrEmpty(prefix) &&
                normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
