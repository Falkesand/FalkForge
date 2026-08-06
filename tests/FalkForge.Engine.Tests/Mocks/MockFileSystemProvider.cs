namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Engine.Detection;

public sealed class MockFileSystemProvider : IFileSystemProvider
{
    private readonly Dictionary<string, Version?> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _throwingDirectories = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Makes <see cref="GetDirectories"/> throw <paramref name="exception"/> when called with
    /// exactly <paramref name="path"/>, simulating an <see cref="IFileSystemProvider"/>
    /// implementation that violates the interface's never-throw contract (an ACL-denied directory,
    /// a TOCTOU race between an existence check and enumeration). Kept separate from
    /// <see cref="_directories"/> so registering a throwing path never also registers it as a real,
    /// enumerable directory.
    /// </summary>
    public MockFileSystemProvider WithThrowingDirectory(string path, Exception exception)
    {
        _throwingDirectories[path] = exception;
        return this;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);
    public bool DirectoryExists(string path) => _directories.Contains(path);
    public Version? GetFileVersion(string path) => _files.TryGetValue(path, out var v) ? v : null;

    // Derives children from the same _directories set WithDirectory populates -- a directory
    // registered via WithDirectory(@"C:\dotnet\shared\Foo\10.0.10") is both individually queryable
    // via DirectoryExists AND enumerable as a child of @"C:\dotnet\shared\Foo" via GetDirectories,
    // matching how the real filesystem needs no separate registration for "exists" vs "list contents".
    public IReadOnlyList<string> GetDirectories(string path)
    {
        if (_throwingDirectories.TryGetValue(path, out var exception))
            throw exception;

        return _directories
            .Where(d => string.Equals(GetParentPath(d), path, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    // Path.GetDirectoryName splits on the host runtime's directory separator(s): on Windows that is
    // '\' (and '/'), but on a non-Windows runtime '\' is not a separator at all, so a Windows-style
    // mock path such as @"C:\dotnet\shared\Foo\10.0.10" -- what production code always seeds this mock
    // with, because the real WindowsFileSystemProvider only ever runs on Windows -- would not split at
    // all there and GetDirectories would silently stop matching. Split on the last literal '\' or '/'
    // ourselves so parent derivation is deterministic regardless of which runtime the test executes on.
    private static string GetParentPath(string path)
    {
        var separatorIndex = path.LastIndexOfAny(['\\', '/']);
        return separatorIndex < 0 ? string.Empty : path[..separatorIndex];
    }
}
