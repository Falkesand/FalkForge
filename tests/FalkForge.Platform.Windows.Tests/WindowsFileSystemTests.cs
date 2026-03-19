using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WindowsFileSystem _fs = new();

    public WindowsFileSystemTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkFsTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FileExists_ExistingFile_ReturnsTrue()
        => Assert.True(_fs.FileExists(MakeFile("a.txt")));

    [Fact]
    public void FileExists_MissingFile_ReturnsFalse()
        => Assert.False(_fs.FileExists(Path.Combine(_tempDir, "missing.txt")));

    [Fact]
    public void DirectoryExists_ExistingDirectory_ReturnsTrue()
        => Assert.True(_fs.DirectoryExists(_tempDir));

    [Fact]
    public void DirectoryExists_MissingDirectory_ReturnsFalse()
        => Assert.False(_fs.DirectoryExists(Path.Combine(_tempDir, "ghost")));

    [Fact]
    public void GetFiles_NonRecursive_DoesNotFindNestedFiles()
    {
        MakeFile("root.txt");
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "n");

        var files = _fs.GetFiles(_tempDir, "*.txt", recursive: false);

        Assert.Contains(files, f => f.EndsWith("root.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith("nested.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetFiles_Recursive_FindsNestedFiles()
    {
        MakeFile("root.txt");
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "nested.txt"), "n");

        var files = _fs.GetFiles(_tempDir, "*.txt", recursive: true);

        Assert.Contains(files, f => f.EndsWith("nested.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetDirectoryName_PathWithParent_ReturnsParent()
    {
        var path = Path.Combine(_tempDir, "file.txt");
        Assert.Equal(_tempDir, _fs.GetDirectoryName(path));
    }

    [Fact]
    public void GetDirectoryName_RootPath_ReturnsEmptyString()
    {
        // Path.GetDirectoryName("C:\\") returns null; null coalescing → string.Empty
        var result = _fs.GetDirectoryName(@"C:\");
        Assert.Equal(string.Empty, result);
    }
}
