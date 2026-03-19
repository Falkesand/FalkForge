namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Engine.Detection;

public sealed class MockFileSystemProvider : IFileSystemProvider
{
    private readonly Dictionary<string, Version?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public MockFileSystemProvider WithFile(string path, Version? version = null)
    {
        _files[path] = version;
        return this;
    }

    public MockFileSystemProvider WithDirectory(string path)
    {
        _directories.Add(path);
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);
    public bool DirectoryExists(string path) => _directories.Contains(path);
    public Version? GetFileVersion(string path) => _files.TryGetValue(path, out var v) ? v : null;
}
