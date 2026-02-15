using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace FalkForge.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> GetFiles(string directory, string pattern, bool recursive) =>
        Directory.GetFiles(directory, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

    public IReadOnlyList<string> GetDirectories(string directory) =>
        Directory.GetDirectories(directory);

    public long GetFileSize(string path) => new FileInfo(path).Length;
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public Stream OpenRead(string path) => File.OpenRead(path);

    public string GetRelativePath(string relativeTo, string path) =>
        Path.GetRelativePath(relativeTo, path);

    public string GetFullPath(string path) => Path.GetFullPath(path);
    public string GetFileName(string path) => Path.GetFileName(path);

    public string GetDirectoryName(string path) =>
        Path.GetDirectoryName(path) ?? string.Empty;

    public string GetFileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
}
