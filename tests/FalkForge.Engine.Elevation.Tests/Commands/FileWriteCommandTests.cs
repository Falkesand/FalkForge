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
