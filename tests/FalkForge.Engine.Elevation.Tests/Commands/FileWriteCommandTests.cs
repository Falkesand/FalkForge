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
