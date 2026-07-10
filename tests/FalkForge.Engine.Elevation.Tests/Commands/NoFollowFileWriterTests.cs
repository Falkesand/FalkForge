using System.Diagnostics;
using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

/// <summary>
/// Tests the handle-based no-follow write layer in isolation. This layer is the INNER
/// enforcement that must hold even when the outer path-based policy checks
/// (<see cref="ElevatedPathPolicy.EnsureDirectoryTreeSafe"/>) have been raced: calling the
/// writer directly with a pre-planted reparse point models the filesystem state an attacker
/// creates by swapping a component AFTER the policy walk and BEFORE the write.
/// </summary>
public sealed class NoFollowFileWriterTests : IDisposable
{
    private readonly string _tempDir;

    public NoFollowFileWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkNoFollowTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Write_WritesContentToRegularPath()
    {
        var targetPath = Path.Combine(_tempDir, "plain.bin");
        var content = new byte[] { 1, 2, 3, 4 };

        var result = NoFollowFileWriter.Write(_tempDir, targetPath, content);

        Assert.True(result.IsSuccess);
        Assert.Equal(content, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void Write_TruncatesExistingLongerFile()
    {
        var targetPath = Path.Combine(_tempDir, "truncate.bin");
        File.WriteAllBytes(targetPath, "AAAAAAAAAAAAAAAA"u8.ToArray());

        var result = NoFollowFileWriter.Write(_tempDir, targetPath, "BB"u8.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal("BB"u8.ToArray(), File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void Write_RejectsDanglingFileSymlinkLeaf_NoFileCreatedAtLinkTarget()
    {
        // Models a post-check plant: a dangling file symlink sits at the target path at the
        // moment the write layer opens it. A follow-open would CREATE the file at the link's
        // target; the no-follow open must open the link itself, see the reparse attribute,
        // and reject.
        var linkTarget = Path.Combine(_tempDir, "not-yet-existing.dll");
        var linkPath = Path.Combine(_tempDir, "dangling.dll");
        if (!TryCreateFileSymlink(linkPath, linkTarget))
            return; // Skip when symlink creation is unavailable

        var result = NoFollowFileWriter.Write(_tempDir, linkPath, new byte[] { 0x4D, 0x5A });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("symbolic link", result.Error.Message);
        Assert.False(File.Exists(linkTarget));
    }

    [Fact]
    public void Write_RejectsExistingFileSymlinkLeaf_LinkTargetUntouched()
    {
        var victimPath = Path.Combine(_tempDir, "victim.txt");
        var victimContent = "victim original bytes"u8.ToArray();
        File.WriteAllBytes(victimPath, victimContent);

        var linkPath = Path.Combine(_tempDir, "link.txt");
        if (!TryCreateFileSymlink(linkPath, victimPath))
            return; // Skip when symlink creation is unavailable

        var result = NoFollowFileWriter.Write(_tempDir, linkPath, new byte[] { 0xFF });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        // The open must not truncate or modify the file behind the link.
        Assert.Equal(victimContent, File.ReadAllBytes(victimPath));
    }

    [Fact]
    public void Write_RejectsParentThatIsAJunction()
    {
        // Models the parent directory itself having been swapped for a junction after the
        // policy walk: the parent handle is opened no-follow, so the junction is seen as a
        // reparse point and rejected — never traversed.
        var junctionTarget = Path.Combine(_tempDir, "realParent");
        var junctionPath = Path.Combine(_tempDir, "swappedParent");
        Directory.CreateDirectory(junctionTarget);
        if (!TryCreateJunction(junctionPath, junctionTarget))
            return; // Skip when junction creation failed

        var result = NoFollowFileWriter.Write(
            junctionPath, Path.Combine(junctionPath, "evil.dll"), new byte[] { 0x4D, 0x5A });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(Path.Combine(junctionTarget, "evil.dll")));
    }

    [Fact]
    public void Write_RejectsParentPathThatResolvesThroughAncestorJunction()
    {
        // Models an ANCESTOR swap: the parent path string still looks legitimate, but path
        // resolution now traverses a junction (only the FINAL component open is no-follow,
        // so an intermediate junction is silently followed by CreateFileW). The handle-level
        // final-path verification must detect that the opened directory's true path differs
        // from the expected one and reject BEFORE any target open.
        var realRoot = Path.Combine(_tempDir, "realRoot");
        var realSub = Path.Combine(realRoot, "sub");
        Directory.CreateDirectory(realSub);

        var junctionPath = Path.Combine(_tempDir, "swappedAncestor");
        if (!TryCreateJunction(junctionPath, realRoot))
            return; // Skip when junction creation failed

        var parentThroughJunction = Path.Combine(junctionPath, "sub");

        var result = NoFollowFileWriter.Write(
            parentThroughJunction, Path.Combine(parentThroughJunction, "evil.dll"), new byte[] { 0x4D, 0x5A });

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.False(File.Exists(Path.Combine(realSub, "evil.dll")));
    }

    private static bool TryCreateFileSymlink(string linkPath, string linkTarget)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, linkTarget);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateJunction(string junctionPath, string junctionTarget)
    {
        // mklink /J creates a junction without elevation on Windows.
        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{junctionTarget}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        })!;
        proc.WaitForExit(5_000);
        return Directory.Exists(junctionPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
