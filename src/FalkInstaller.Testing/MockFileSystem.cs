using FalkInstaller.Platform;

namespace FalkInstaller.Testing;

public sealed class MockFileSystem : IFileSystem
{
    private readonly Dictionary<string, MockFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public MockFileSystem AddFile(string path, byte[]? content = null, long? size = null)
    {
        var fullPath = NormalizePath(path);
        _files[fullPath] = new MockFile(content ?? [], size ?? content?.Length ?? 0);

        // Auto-add parent directories
        var dir = GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(dir))
        {
            _directories.Add(dir);
            dir = GetDirectoryName(dir);
        }

        return this;
    }

    public MockFileSystem AddDirectory(string path)
    {
        _directories.Add(NormalizePath(path));
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(NormalizePath(path));

    public bool DirectoryExists(string path) => _directories.Contains(NormalizePath(path));

    public IReadOnlyList<string> GetFiles(string directory, string pattern, bool recursive)
    {
        var normalizedDir = NormalizePath(directory);
        return _files.Keys
            .Where(f => recursive
                ? f.StartsWith(normalizedDir + "/", StringComparison.OrdinalIgnoreCase)
                : GetDirectoryName(f).Equals(normalizedDir, StringComparison.OrdinalIgnoreCase))
            .Where(f => MatchesPattern(GetFileName(f), pattern))
            .ToList();
    }

    public IReadOnlyList<string> GetDirectories(string directory)
    {
        var normalizedDir = NormalizePath(directory);
        return _directories
            .Where(d => GetDirectoryName(d).Equals(normalizedDir, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public long GetFileSize(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.TryGetValue(normalizedPath, out var file) ? file.Size : 0;
    }

    public byte[] ReadAllBytes(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.TryGetValue(normalizedPath, out var file) ? file.Content : throw new FileNotFoundException(path);
    }

    public Stream OpenRead(string path)
    {
        return new MemoryStream(ReadAllBytes(path));
    }

    public string GetRelativePath(string relativeTo, string path)
    {
        var from = NormalizePath(relativeTo).TrimEnd('/');
        var to = NormalizePath(path);
        if (to.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase))
            return to[(from.Length + 1)..];
        return to;
    }

    public string GetFullPath(string path) => NormalizePath(path);

    public string GetFileName(string path)
    {
        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

    public string GetDirectoryName(string path)
    {
        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[..lastSlash] : string.Empty;
    }

    public string GetFileHash(string path)
    {
        var bytes = ReadAllBytes(path);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public DateTime GetLastWriteTimeUtc(string path) => DateTime.UtcNow;

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (pattern == "*" || pattern == "*.*") return true;
        if (pattern.StartsWith("*."))
        {
            var ext = pattern[1..];
            return fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        }
        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MockFile(byte[] Content, long Size);
}
