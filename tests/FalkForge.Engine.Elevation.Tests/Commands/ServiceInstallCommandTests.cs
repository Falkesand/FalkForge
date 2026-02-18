using FalkForge.Engine.Elevation.Commands;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class ServiceInstallCommandTests
{
    [Fact]
    public void Execute_RejectsPathOutsideTrustedDirectories()
    {
        var command = new ServiceInstallCommand();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("MySvc");
        writer.Write("My Service");
        writer.Write(@"C:\Users\attacker\evil.exe");
        var payload = stream.ToArray();

        var result = command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
