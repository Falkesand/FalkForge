using System.Diagnostics;
using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class FileWriteCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileWriteCommand _command;

    public FileWriteCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkElevationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _command = new FileWriteCommand();
    }

    [Fact]
    public void Execute_WritesFileSuccessfully()
    {
        var targetPath = Path.Combine(_tempDir, "test.txt");
        var content = "Hello, World!"u8.ToArray();
        var payload = BuildPayload(targetPath, content);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(content, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void Execute_CreatesParentDirectoryIfMissing()
    {
        var targetPath = Path.Combine(_tempDir, "sub", "deep", "test.txt");
        var content = new byte[] { 1, 2, 3 };
        var payload = BuildPayload(targetPath, content);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(targetPath));
        Assert.Equal(content, File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void Execute_ReturnsFailureForInvalidPath()
    {
        var targetPath = Path.Combine(_tempDir, "invalid\0name.txt");
        var content = new byte[] { 1 };
        var payload = BuildPayload(targetPath, content);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void Execute_WritesEmptyFileSuccessfully()
    {
        var targetPath = Path.Combine(_tempDir, "empty.bin");
        var payload = BuildPayload(targetPath, Array.Empty<byte>());

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(targetPath));
        Assert.Empty(File.ReadAllBytes(targetPath));
    }

    [Fact]
    public void Execute_RejectsDirectoryJunction()
    {
        var junctionTarget = Path.Combine(_tempDir, "junctionTarget");
        var junctionPath = Path.Combine(_tempDir, "junction");
        Directory.CreateDirectory(junctionTarget);

        // mklink /J creates a junction without elevation on Windows
        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{junctionTarget}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        })!;
        proc.WaitForExit(5_000);

        if (!Directory.Exists(junctionPath))
            return; // Skip if junction creation failed

        var targetPath = Path.Combine(junctionPath, "evil.dll");
        var payload = BuildPayload(targetPath, new byte[] { 0x4D, 0x5A });

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("symbolic link or junction", result.Error.Message);
    }

    [Fact]
    public void Execute_RejectsAncestorDirectoryJunction()
    {
        // Attack: an attacker pre-creates a junction at an ANCESTOR level under an allowed,
        // user-writable root. Directory.CreateDirectory would happily walk THROUGH the
        // junction, landing elevated bytes at the junction's target. The leaf-only reparse
        // check does not catch this because the leaf (evil/dropped.dll's parent) is created
        // fresh below the junction. FIX 1: reject when ANY ancestor is a reparse point.
        var junctionTarget = Path.Combine(_tempDir, "realTarget");
        var ancestorJunction = Path.Combine(_tempDir, "ancestor");
        Directory.CreateDirectory(junctionTarget);

        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{ancestorJunction}\" \"{junctionTarget}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        })!;
        proc.WaitForExit(5_000);

        if (!Directory.Exists(ancestorJunction))
            return; // Skip if junction creation failed (e.g., filesystem without reparse support)

        // The target sits UNDER a not-yet-existing subdirectory of the ancestor junction.
        var targetPath = Path.Combine(ancestorJunction, "sub", "evil.dll");
        var payload = BuildPayload(targetPath, new byte[] { 0x4D, 0x5A });

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("symbolic link or junction", result.Error.Message);

        // No bytes must have escaped through the junction into the target directory.
        Assert.False(File.Exists(Path.Combine(junctionTarget, "sub", "evil.dll")));
    }

    [Fact]
    public void Execute_RejectsDanglingFileSymlinkTarget()
    {
        // Attack: the attacker plants a DANGLING file symlink at the target path, pointing
        // at a not-yet-existing file in a forbidden location (e.g. a DLL name in System32).
        // A path-based existence check (File.Exists) FOLLOWS the link, sees "nothing there",
        // and a path-based write (File.WriteAllBytes) then CREATES the file at the link's
        // target — an elevated write redirected outside the allowed root. The write must
        // instead open the final component no-follow and reject the reparse point.
        var outsideDir = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outsideDir);
        var linkTarget = Path.Combine(outsideDir, "planted.dll"); // deliberately does NOT exist
        var linkPath = Path.Combine(_tempDir, "dangling-link.dll");

        if (!TryCreateFileSymlink(linkPath, linkTarget))
            return; // Skip when symlink creation is unavailable (no privilege / no dev mode)

        var payload = BuildPayload(linkPath, new byte[] { 0x4D, 0x5A });

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("symbolic link", result.Error.Message);

        // No bytes may land at the link's target: the elevated write must not follow the link.
        Assert.False(File.Exists(linkTarget));
    }

    [Fact]
    public void Execute_RejectsFileSymlinkTargetWithoutModifyingLinkTarget()
    {
        // A file symlink to an EXISTING file must be rejected without truncating or
        // overwriting the file behind the link.
        var victimPath = Path.Combine(_tempDir, "victim.txt");
        var victimContent = "original victim content"u8.ToArray();
        File.WriteAllBytes(victimPath, victimContent);

        var linkPath = Path.Combine(_tempDir, "link-to-victim.txt");
        if (!TryCreateFileSymlink(linkPath, victimPath))
            return; // Skip when symlink creation is unavailable

        var payload = BuildPayload(linkPath, new byte[] { 0xFF });

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(victimContent, File.ReadAllBytes(victimPath));
    }

    [Fact]
    public void Execute_OverwritesExistingFileWithShorterContent()
    {
        // Overwrite semantics: an existing regular file is replaced entirely — a shorter
        // second write must not leave trailing bytes from the first write.
        var targetPath = Path.Combine(_tempDir, "overwrite.txt");
        var first = _command.Execute(BuildPayload(targetPath, "AAAAAAAAAAAAAAAA"u8.ToArray()));
        Assert.True(first.IsSuccess);

        var second = _command.Execute(BuildPayload(targetPath, "BB"u8.ToArray()));

        Assert.True(second.IsSuccess);
        Assert.Equal("BB"u8.ToArray(), File.ReadAllBytes(targetPath));
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

    private static byte[] BuildPayload(string targetPath, byte[] content)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(targetPath);
        writer.Write(content.Length);
        writer.Write(content);
        writer.Flush();
        return stream.ToArray();
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
