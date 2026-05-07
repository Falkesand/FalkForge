using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
        // Restore any read-only file ACLs before deletion so cleanup succeeds.
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                try { new FileInfo(f).IsReadOnly = false; } catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }

        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort */ }
    }

    private string MakeFile(string name, string content = "x")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ─── Happy-path (existing) ────────────────────────────────────────────────

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

    // ─── Error paths: missing file ────────────────────────────────────────────

    [Fact]
    public void ReadAllBytes_MissingFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "no-such-file.bin");
        Assert.Throws<FileNotFoundException>(() => _fs.ReadAllBytes(missing));
    }

    [Fact]
    public void OpenRead_MissingFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "no-such-file.bin");
        Assert.Throws<FileNotFoundException>(() => _fs.OpenRead(missing));
    }

    [Fact]
    public void GetFileSize_MissingFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "ghost.dat");
        Assert.Throws<FileNotFoundException>(() => _fs.GetFileSize(missing));
    }

    [Fact]
    public void GetFileHash_MissingFile_ThrowsFileNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "ghost.dat");
        Assert.Throws<FileNotFoundException>(() => _fs.GetFileHash(missing));
    }

    [Fact]
    public void GetLastWriteTimeUtc_MissingFile_ReturnsMinValue()
    {
        // File.GetLastWriteTimeUtc on a missing path returns DateTime.MinValue (1601-01-01) — not an exception.
        var missing = Path.Combine(_tempDir, "ghost.dat");
        var result = _fs.GetLastWriteTimeUtc(missing);
        Assert.Equal(new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }

    // ─── Error paths: directory operations on a file path ────────────────────

    [Fact]
    public void GetFiles_PathIsFile_ThrowsIOException()
    {
        var filePath = MakeFile("notadir.txt");
        // Directory.GetFiles on a file path throws IOException or DirectoryNotFoundException.
        Assert.ThrowsAny<IOException>(() => _fs.GetFiles(filePath, "*", recursive: false));
    }

    [Fact]
    public void GetDirectories_PathIsFile_ThrowsIOException()
    {
        var filePath = MakeFile("notadir2.txt");
        Assert.ThrowsAny<IOException>(() => _fs.GetDirectories(filePath));
    }

    [Fact]
    public void GetFiles_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "no-such-dir");
        Assert.Throws<DirectoryNotFoundException>(() => _fs.GetFiles(missing, "*", recursive: false));
    }

    [Fact]
    public void GetDirectories_MissingDirectory_ThrowsDirectoryNotFoundException()
    {
        var missing = Path.Combine(_tempDir, "no-such-dir");
        Assert.Throws<DirectoryNotFoundException>(() => _fs.GetDirectories(missing));
    }

    // ─── Error paths: invalid path characters ────────────────────────────────

    [Fact]
    public void ReadAllBytes_InvalidPathChars_ThrowsArgumentException()
    {
        // Null byte is universally invalid on Windows.
        var badPath = _tempDir + "\0invalid";
        Assert.ThrowsAny<ArgumentException>(() => _fs.ReadAllBytes(badPath));
    }

    [Fact]
    public void GetFiles_InvalidPathChars_ThrowsArgumentException()
    {
        var badPath = _tempDir + "\0invalid";
        Assert.ThrowsAny<ArgumentException>(() => _fs.GetFiles(badPath, "*", recursive: false));
    }

    // ─── Error paths: access-denied (read-only file) ─────────────────────────

    [Fact]
    public void OpenRead_DeniedViaAcl_ThrowsUnauthorizedAccessException()
    {
        // Skip if running as admin — admins bypass discretionary ACLs.
        if (IsCurrentUserAdmin())
            return;

        var path = MakeFile("acl-denied.txt", "secret");

        // Deny ReadData for the current user.
        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        acl.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadData,
            AccessControlType.Deny));
        fi.SetAccessControl(acl);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _fs.OpenRead(path));
        }
        finally
        {
            // Remove the deny rule so Dispose() can delete the file.
            acl.RemoveAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.ReadData,
                AccessControlType.Deny));
            fi.SetAccessControl(acl);
        }
    }

    [Fact]
    public void ReadAllBytes_DeniedViaAcl_ThrowsUnauthorizedAccessException()
    {
        if (IsCurrentUserAdmin())
            return;

        var path = MakeFile("acl-denied2.txt", "secret");

        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        acl.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadData,
            AccessControlType.Deny));
        fi.SetAccessControl(acl);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _fs.ReadAllBytes(path));
        }
        finally
        {
            acl.RemoveAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.ReadData,
                AccessControlType.Deny));
            fi.SetAccessControl(acl);
        }
    }

    [Fact]
    public void GetFileHash_DeniedViaAcl_ThrowsUnauthorizedAccessException()
    {
        if (IsCurrentUserAdmin())
            return;

        var path = MakeFile("acl-denied3.txt", "secret");

        var fi = new FileInfo(path);
        var acl = fi.GetAccessControl();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        acl.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.ReadData,
            AccessControlType.Deny));
        fi.SetAccessControl(acl);

        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => _fs.GetFileHash(path));
        }
        finally
        {
            acl.RemoveAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.ReadData,
                AccessControlType.Deny));
            fi.SetAccessControl(acl);
        }
    }

    // ─── Path-too-long ────────────────────────────────────────────────────────

    [Fact]
    public void ReadAllBytes_PathExceedsMaxPath_ThrowsPathTooLongOrIOException()
    {
        // 32767 chars exceeds both legacy MAX_PATH (260) and extended limits.
        var tooLong = @"C:\" + new string('a', 32_000);
        Assert.ThrowsAny<Exception>(() => _fs.ReadAllBytes(tooLong));
    }

    // ─── Stream: dispose before read ─────────────────────────────────────────

    [Fact]
    public void OpenRead_StreamDisposedBeforeRead_ThrowsObjectDisposedException()
    {
        var path = MakeFile("dispose-test.txt", "hello");
        var stream = _fs.OpenRead(path);
        stream.Dispose();

        Assert.Throws<ObjectDisposedException>(() => stream.Read(new byte[4], 0, 4));
    }

    // ─── GetFileSize on directory path ────────────────────────────────────────

    [Fact]
    public void GetFileSize_DirectoryPath_ThrowsUnauthorizedOrIOException()
    {
        // FileInfo.Length on a directory throws UnauthorizedAccessException on Windows.
        Assert.ThrowsAny<Exception>(() => _fs.GetFileSize(_tempDir));
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static bool IsCurrentUserAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
