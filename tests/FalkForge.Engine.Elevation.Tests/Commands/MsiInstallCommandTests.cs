using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class MsiInstallCommandTests
{
    private readonly MsiInstallCommand _command = new();

    [Theory]
    [InlineData(@"\\server\share\evil.msi")]
    [InlineData(@"\\.\pipe\evil")]
    [InlineData(@"\\?\UNC\server\share\evil.msi")]
    public void Execute_RejectsUncPaths(string path)
    {
        var payload = BuildPayload(path, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("UNC", result.Error.Message);
    }

    private static byte[] BuildPayload(string msiPath, string additionalArgs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Flush();
        return stream.ToArray();
    }
}
